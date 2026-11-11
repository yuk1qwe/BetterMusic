use serde::{Deserialize, Serialize};
use url::Url;

pub const HOME: &str = "\0feather:newtab";
pub const MAX_RESTORED_TABS: usize = 50;

// Никогда не оставляем в строке адрес предыдущего сайта после навигации.
pub fn display_address(raw: &str, is_home: bool) -> String {
    if is_home {
        return "Новая вкладка · страница Feather".into();
    }
    let Ok(mut url) = Url::parse(raw) else {
        return "Неизвестный адрес страницы".into();
    };
    match url.scheme() {
        "http" | "https" => {
            if !url.username().is_empty() || url.password().is_some() {
                let _ = url.set_password(None);
                let _ = url.set_username("");
                format!("⚠ Учётные данные в адресе · {}", url.as_str())
            } else {
                url.as_str().to_string()
            }
        }
        "data" => "data: · содержимое страницы".into(),
        "about" => url.as_str().to_string(),
        scheme => format!("{scheme}: · нестандартный адрес"),
    }
}
pub fn restorable(raw: &str) -> String {
    if raw == HOME || web_url(raw) {
        raw.to_string()
    } else {
        HOME.to_string()
    }
}
pub fn tab_caption(raw: &str) -> String {
    if raw == HOME {
        return "Новая вкладка".into();
    }
    Url::parse(raw)
        .ok()
        .and_then(|u| u.host_str().map(str::to_string))
        .unwrap_or_else(|| "Страница".into())
        .chars()
        .take(18)
        .collect()
}
#[derive(Clone, Serialize, Deserialize, Debug, PartialEq, Eq)]
pub struct Entry {
    pub title: String,
    pub url: String,
}
#[derive(Clone, Serialize, Deserialize, Debug)]
#[serde(default)]
pub struct Data {
    pub tabs: Vec<String>,
    pub active: usize,
    pub bookmarks: Vec<Entry>,
    pub history: Vec<Entry>,
    pub hard: bool,
}

impl Default for Data {
    fn default() -> Self {
        Self {
            tabs: vec![],
            active: 0,
            bookmarks: vec![],
            history: vec![],
            hard: true,
        }
    }
}

pub fn web_url(s: &str) -> bool {
    Url::parse(s).is_ok_and(|u| {
        matches!(u.scheme(), "http" | "https")
            && u.host_str().is_some()
            && u.username().is_empty()
            && u.password().is_none()
    })
}
pub fn navigate(input: &str) -> Option<String> {
    let s = input.trim();
    if s.is_empty() {
        return None;
    }
    if web_url(s) {
        return Some(s.to_string());
    }
    // Явные неприемлемые схемы не исполняются и не передаются оболочке.
    if s.contains("://")
        || s.starts_with("javascript:")
        || s.starts_with("file:")
        || s.starts_with("data:")
    {
        return None;
    }
    if !s.contains(char::is_whitespace)
        && (s.contains('.') || s.starts_with("localhost") || s.starts_with("127.0.0.1"))
    {
        let candidate = format!("https://{s}");
        if web_url(&candidate) {
            return Some(candidate);
        }
    }
    let mut url = Url::parse("https://www.google.com/search").unwrap();
    url.query_pairs_mut().append_pair("q", s);
    Some(url.into())
}
impl Data {
    pub fn add_history(&mut self, title: String, url: String) {
        if !web_url(&url) {
            return;
        }
        if self.history.first().is_some_and(|e| e.url == url) {
            return;
        }
        self.history.insert(0, Entry { title, url });
        self.history.truncate(500);
    }
    pub fn toggle_bookmark(&mut self, entry: Entry) {
        if !web_url(&entry.url) {
            return;
        }
        if let Some(i) = self.bookmarks.iter().position(|e| e.url == entry.url) {
            self.bookmarks.remove(i);
        } else {
            self.bookmarks.insert(0, entry);
        }
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn navigation_is_safe_and_search_is_encoded() {
        assert_eq!(navigate("example.com/a").unwrap(), "https://example.com/a");
        assert_eq!(
            navigate("http://localhost:8000/").unwrap(),
            "http://localhost:8000/"
        );
        assert!(navigate("кот & собака").unwrap().contains("%26"));
        assert_eq!(navigate("javascript:alert(1)"), None);
        assert_eq!(navigate("file:///C:/secret"), None);
        assert!(!web_url("https://user:secret@example.com/"));
    }
    #[test]
    fn origin_display_never_reuses_the_previous_address() {
        assert_eq!(
            display_address("data:text/html,example", false),
            "data: · содержимое страницы"
        );
        assert_eq!(display_address("about:blank", false), "about:blank");
        assert_eq!(
            display_address("data:text/html,anything", true),
            "Новая вкладка · страница Feather"
        );
        assert!(display_address("https://пример.рф/a", false).contains("xn--"));
        let with_credentials = display_address("https://user:secret@example.com/", false);
        assert!(with_credentials.contains("example.com"));
        assert!(!with_credentials.contains("secret"));
        assert!(with_credentials.starts_with('⚠'));
        assert_eq!(restorable("javascript:alert(1)"), HOME);
        assert_eq!(restorable(HOME), HOME);
        assert_ne!(
            display_address("feather:newtab", false),
            display_address(HOME, true)
        );
    }
    #[test]
    fn bookmarks_toggle_and_history_are_bounded() {
        let mut d = Data::default();
        let e = Entry {
            title: "Тест".into(),
            url: "https://example.com/".into(),
        };
        d.toggle_bookmark(e.clone());
        assert_eq!(d.bookmarks.len(), 1);
        d.toggle_bookmark(e);
        assert!(d.bookmarks.is_empty());
        for i in 0..510 {
            d.add_history(i.to_string(), format!("https://example.com/{i}"));
        }
        assert_eq!(d.history.len(), 500);
        d.add_history("repeat".into(), "https://example.com/509".into());
        assert_eq!(d.history.len(), 500);
    }
    #[test]
    fn start_page_has_no_privileged_script_or_remote_assets() {
        let page = include_str!("../assets/new_tab.html");
        assert!(!page.to_ascii_lowercase().contains("<script"));
        assert!(page.contains("default-src 'none'"));
        assert!(page.contains("img-src data:"));
        assert!(page.contains("{{BACKGROUND}}"));
    }
    #[test]
    fn old_or_partial_settings_load() {
        let d: Data = serde_json::from_str("{\"hard\":true}").unwrap();
        assert!(d.hard);
        assert!(d.tabs.is_empty());
    }
}
