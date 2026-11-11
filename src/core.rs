use serde::{Deserialize, Serialize};
use url::Url;

pub const HOME: &str = "https://www.wikipedia.org/";
#[derive(Clone, Serialize, Deserialize, Debug, PartialEq, Eq)]
pub struct Entry { pub title: String, pub url: String }
#[derive(Clone, Serialize, Deserialize, Debug, Default)]
#[serde(default)]
pub struct Data { pub tabs: Vec<String>, pub active: usize, pub bookmarks: Vec<Entry>, pub history: Vec<Entry>, pub hard: bool }

pub fn web_url(s: &str) -> bool {
    Url::parse(s).is_ok_and(|u| matches!(u.scheme(), "http" | "https") && u.host_str().is_some() && u.username().is_empty() && u.password().is_none())
}
pub fn navigate(input: &str) -> Option<String> {
    let s = input.trim();
    if s.is_empty() { return None; }
    if web_url(s) { return Some(s.to_string()); }
    // Явные неприемлемые схемы не исполняются и не передаются оболочке.
    if s.contains("://") || s.starts_with("javascript:") || s.starts_with("file:") || s.starts_with("data:") { return None; }
    if !s.contains(char::is_whitespace) && (s.contains('.') || s.starts_with("localhost") || s.starts_with("127.0.0.1")) {
        let candidate = format!("https://{s}");
        if web_url(&candidate) { return Some(candidate); }
    }
    let mut url = Url::parse("https://www.google.com/search").unwrap();
    url.query_pairs_mut().append_pair("q", s);
    Some(url.into())
}
impl Data {
    pub fn add_history(&mut self, title: String, url: String) {
        if !web_url(&url) { return; }
        if self.history.first().is_some_and(|e| e.url == url) { return; }
        self.history.insert(0, Entry { title, url });
        self.history.truncate(500);
    }
    pub fn toggle_bookmark(&mut self, entry: Entry) {
        if !web_url(&entry.url) { return; }
        if let Some(i) = self.bookmarks.iter().position(|e| e.url == entry.url) { self.bookmarks.remove(i); }
        else { self.bookmarks.insert(0, entry); }
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn navigation_is_safe_and_search_is_encoded() {
        assert_eq!(navigate("example.com/a").unwrap(), "https://example.com/a");
        assert_eq!(navigate("http://localhost:8000/").unwrap(), "http://localhost:8000/");
        assert!(navigate("кот & собака").unwrap().contains("%26"));
        assert_eq!(navigate("javascript:alert(1)"), None);
        assert_eq!(navigate("file:///C:/secret"), None);
        assert!(!web_url("https://user:secret@example.com/"));
    }
    #[test] fn bookmarks_toggle_and_history_are_bounded() {
        let mut d=Data::default(); let e=Entry {title:"Тест".into(),url:"https://example.com/".into()};
        d.toggle_bookmark(e.clone()); assert_eq!(d.bookmarks.len(),1); d.toggle_bookmark(e); assert!(d.bookmarks.is_empty());
        for i in 0..510 { d.add_history(i.to_string(),format!("https://example.com/{i}")); }
        assert_eq!(d.history.len(),500); d.add_history("repeat".into(),"https://example.com/509".into()); assert_eq!(d.history.len(),500);
    }
    #[test] fn old_or_partial_settings_load() { let d:Data=serde_json::from_str("{\"hard\":true}").unwrap(); assert!(d.hard); assert!(d.tabs.is_empty()); }
}
