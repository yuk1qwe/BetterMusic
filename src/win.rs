use crate::core::{self, Data, Entry};
use cef::*;
use std::{cell::RefCell, path::PathBuf, rc::Rc};

// Интерфейс реализован нативными CEF Views. Страницы не получают канал к Rust.
type Shared = Rc<RefCell<BrowserState>>;
struct Tab {
    id: u32,
    view: BrowserView,
    button: LabelButton,
    url: String,
    title: String,
}
struct BrowserState {
    data: Data,
    tabs: Vec<Tab>,
    active: u32,
    next: u32,
    window: Option<Window>,
    bar: Option<Panel>,
    content: Option<Panel>,
    extra: Option<Panel>,
    address: Option<Textfield>,
    closing: bool,
    browsers: usize,
}
fn profile() -> PathBuf {
    if let Some(p) = std::env::var_os("SEVER_PROFILE") {
        return PathBuf::from(p);
    }
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(std::env::temp_dir)
        .join("SeverBrowser")
}
fn trace(event: &str) {
    if std::env::var_os("SEVER_LAYOUT_DEBUG").is_some() {
        use std::io::Write;
        if let Ok(mut f) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(profile().join("lifecycle.txt"))
        {
            let _ = writeln!(f, "{event}");
        }
    }
}
fn read_data() -> Data {
    std::fs::read(profile().join("state.json"))
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .unwrap_or_default()
}
fn save(s: &BrowserState) {
    if s.closing {
        return;
    }
    let mut data = s.data.clone();
    data.tabs = s
        .tabs
        .iter()
        .map(|t| t.url.clone())
        .filter(|u| core::web_url(u))
        .collect();
    data.active = s.tabs.iter().position(|t| t.id == s.active).unwrap_or(0);
    let dir = profile();
    if std::fs::create_dir_all(&dir).is_ok() {
        if let Ok(bytes) = serde_json::to_vec_pretty(&data) {
            let temp = dir.join("state.tmp");
            if std::fs::write(&temp, bytes).is_ok() {
                let _ = std::fs::rename(temp, dir.join("state.json"));
            }
        }
    }
}
fn current(s: &Shared) -> Option<Browser> {
    let st = s.borrow();
    st.tabs
        .iter()
        .find(|t| t.id == st.active)
        .and_then(|t| t.view.browser())
}
fn switch(s: &Shared, id: u32) {
    let (views, address, url, window) = {
        let mut st = s.borrow_mut();
        st.active = id;
        let views = st
            .tabs
            .iter()
            .map(|t| (t.view.clone(), t.button.clone(), t.id == id))
            .collect::<Vec<_>>();
        let url = st
            .tabs
            .iter()
            .find(|t| t.id == id)
            .map(|t| t.url.clone())
            .unwrap_or_default();
        (views, st.address.clone(), url, st.window.clone())
    };
    for (view, button, active) in views {
        view.set_visible(active as i32);
        button.set_background_color(if active { 0xffcbdcf4 } else { 0xffe9f0fa });
        button.set_enabled_text_colors(if active { 0xff174b93 } else { 0xff34465c });
    }
    if let Some(a) = address {
        a.set_text(Some(&CefString::from(url.as_str())));
    }
    if let Some(w) = window {
        w.invalidate_layout();
        w.layout();
    }
    let (content, view) = {
        let st = s.borrow();
        (
            st.content.clone(),
            st.tabs.iter().find(|t| t.id == id).map(|t| t.view.clone()),
        )
    };
    if let Some(p) = content.as_ref() {
        p.invalidate_layout();
        p.layout();
    }
    if std::env::var_os("SEVER_LAYOUT_DEBUG").is_some() {
        let info = format!(
            "content={:?}, view={:?}\n",
            content.map(|p| p.bounds()),
            view.map(|v| v.bounds())
        );
        let _ = std::fs::write(profile().join("layout.txt"), info);
    }
    save(&s.borrow());
}
fn button(text: &str, action: Action, s: &Shared) -> LabelButton {
    let mut d = Buttons::new(s.clone(), action);
    let button = label_button_create(Some(&mut d), Some(&CefString::from(text))).expect("кнопка");
    button.set_font_list(Some(&CefString::from("Segoe UI, 13px")));
    button.set_enabled_text_colors(0xff263c58);
    button.set_minimum_size(Some(&Size {
        width: 0,
        height: 34,
    }));
    button
}
fn make_tab(s: &Shared, url: &str) {
    let (id, bar, content) = {
        let mut st = s.borrow_mut();
        let id = st.next;
        st.next += 1;
        (id, st.bar.clone().unwrap(), st.content.clone().unwrap())
    };
    let mut client = BrowserClient::new(s.clone(), id);
    let mut delegate = BrowserViewEvents::new();
    let view = browser_view_create(
        Some(&mut client),
        Some(&CefString::from(url)),
        Some(&BrowserSettings::default()),
        None,
        None,
        Some(&mut delegate),
    )
    .expect("вкладка");
    let btn = button("Новая вкладка", Action::Tab(id), s);
    {
        let mut st = s.borrow_mut();
        st.tabs.push(Tab {
            id,
            view: view.clone(),
            button: btn.clone(),
            url: url.into(),
            title: "Новая вкладка".into(),
        });
    }
    bar.add_child_view(Some(&mut View::from(&btn)));
    content.add_child_view(Some(&mut View::from(&view)));
    switch(s, id);
}
fn navigate(s: &Shared, text: &str) {
    if let (Some(u), Some(b)) = (core::navigate(text), current(s)) {
        if let Some(frame) = b.main_frame() {
            frame.load_url(Some(&CefString::from(u.as_str())));
        }
    }
}
fn external(s: &Shared) {
    let url = {
        let st = s.borrow();
        st.tabs
            .iter()
            .find(|t| t.id == st.active)
            .map(|t| t.url.clone())
            .unwrap_or_default()
    };
    if !core::web_url(&url) {
        return;
    }
    use std::os::windows::ffi::OsStrExt;
    let wide: Vec<u16> = std::ffi::OsStr::new(&url)
        .encode_wide()
        .chain(Some(0))
        .collect();
    unsafe {
        windows_sys::Win32::UI::Shell::ShellExecuteW(
            std::ptr::null_mut(),
            std::ptr::null(),
            wide.as_ptr(),
            std::ptr::null(),
            std::ptr::null(),
            windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL,
        );
    }
}
#[derive(Clone)]
enum Action {
    New,
    Close,
    Back,
    Forward,
    Reload,
    Go,
    Tab(u32),
    Mode,
    Bookmark,
    History,
    Bookmarks,
    ListPage(bool, usize),
    Open(String),
    External,
}
fn act(s: &Shared, action: Action) {
    // Любое новое действие после отмены диалога beforeunload возобновляет сохранение.
    {
        let mut st = s.borrow_mut();
        if st.closing {
            st.closing = false;
            save(&st);
        }
    }
    match action {
        Action::New => make_tab(s, core::HOME),
        Action::Tab(id) => switch(s, id),
        Action::Close => {
            if let Some(b) = current(s) {
                if let Some(h) = b.host() {
                    h.close_browser(0);
                }
            }
        }
        Action::Back => {
            if let Some(b) = current(s) {
                b.go_back();
            }
        }
        Action::Forward => {
            if let Some(b) = current(s) {
                b.go_forward();
            }
        }
        Action::Reload => {
            if let Some(b) = current(s) {
                b.reload();
            }
        }
        Action::Go => {
            let text = {
                s.borrow()
                    .address
                    .as_ref()
                    .map(|a| CefString::from(&a.text()).to_string())
                    .unwrap_or_default()
            };
            navigate(s, &text);
        }
        Action::Mode => {
            let (extra, hard) = {
                let mut st = s.borrow_mut();
                st.data.hard = !st.data.hard;
                (st.extra.clone(), st.data.hard)
            };
            if let Some(p) = extra {
                p.set_visible(hard as i32);
            }
            if let Some(w) = s.borrow().window.clone() {
                w.layout();
            }
            save(&s.borrow());
        }
        Action::Bookmark => {
            let mut st = s.borrow_mut();
            if let Some(t) = st.tabs.iter().find(|t| t.id == st.active) {
                let e = Entry {
                    title: t.title.clone(),
                    url: t.url.clone(),
                };
                st.data.toggle_bookmark(e);
            }
            save(&st);
        }
        Action::Bookmarks => show_list(s, true, 0),
        Action::History => show_list(s, false, 0),
        Action::ListPage(bookmarks, page) => show_list(s, bookmarks, page),
        Action::Open(url) => navigate(s, &url),
        Action::External => external(s),
    }
}
fn show_list(s: &Shared, bookmarks: bool, page: usize) {
    // Список строится только по запросу, без фоновых страниц и сценариев.
    let (panel, items) = {
        let st = s.borrow();
        (
            st.extra.clone(),
            if bookmarks {
                st.data.bookmarks.clone()
            } else {
                st.data.history.clone()
            },
        )
    };
    if let Some(p) = panel {
        p.remove_all_child_views();
        p.add_child_view(Some(&mut View::from(&button(
            "★ Сохранить / убрать",
            Action::Bookmark,
            s,
        ))));
        p.add_child_view(Some(&mut View::from(&button(
            "Закладки",
            Action::Bookmarks,
            s,
        ))));
        p.add_child_view(Some(&mut View::from(&button(
            "История",
            Action::History,
            s,
        ))));
        p.add_child_view(Some(&mut View::from(&button(
            "Открыть в системе",
            Action::External,
            s,
        ))));
        let pages = items.len().div_ceil(4);
        let page = page.min(pages.saturating_sub(1));
        if page > 0 {
            p.add_child_view(Some(&mut View::from(&button(
                "←",
                Action::ListPage(bookmarks, page - 1),
                s,
            ))));
        }
        for e in items.into_iter().skip(page * 4).take(4) {
            let label: String = if e.title.is_empty() {
                e.url.clone()
            } else {
                e.title.clone()
            }
            .chars()
            .take(12)
            .collect();
            p.add_child_view(Some(&mut View::from(&button(
                &label,
                Action::Open(e.url),
                s,
            ))));
        }
        if page + 1 < pages {
            p.add_child_view(Some(&mut View::from(&button(
                "Ещё →",
                Action::ListPage(bookmarks, page + 1),
                s,
            ))));
        }
        if let Some(w) = s.borrow().window.clone() {
            w.layout();
        }
    }
}
wrap_button_delegate! { struct Buttons { state: Shared, action: Action }
impl ViewDelegate {} impl ButtonDelegate { fn on_button_pressed(&self,_unused1:Option<&mut Button>){act(&self.state,self.action.clone());} } }
wrap_textfield_delegate! { struct Address { state: Shared }
impl ViewDelegate {} impl TextfieldDelegate { fn on_key_event(&self,_unused2:Option<&mut Textfield>,event:Option<&KeyEvent>)->i32 {
   if event.is_some_and(|e|e.type_==KeyEventType::RAWKEYDOWN && e.windows_key_code==13) {act(&self.state,Action::Go);1}else{0}
} } }
wrap_browser_view_delegate! { struct BrowserViewEvents {} impl ViewDelegate {} impl BrowserViewDelegate {
 fn browser_runtime_style(&self)->RuntimeStyle {RuntimeStyle::ALLOY}
} }
wrap_client! { struct BrowserClient { state: Shared, id: u32 } impl Client {
 fn display_handler(&self)->Option<DisplayHandler>{Some(DisplayEvents::new(self.state.clone(),self.id))}
 fn life_span_handler(&self)->Option<LifeSpanHandler>{Some(LifeEvents::new(self.state.clone(),self.id))}
 fn download_handler(&self)->Option<DownloadHandler>{Some(Downloads::new())}
} }
wrap_download_handler! { struct Downloads; impl DownloadHandler {
 fn can_download(&self,_unused3:Option<&mut Browser>,_unused4:Option<&CefString>,_unused5:Option<&CefString>)->i32{1}
 fn on_before_download(&self,_unused6:Option<&mut Browser>,_unused7:Option<&mut DownloadItem>,_unused8:Option<&CefString>,callback:Option<&mut BeforeDownloadCallback>)->i32 { if let Some(cb)=callback {cb.cont(None,1);1}else{0} }
} }
wrap_display_handler! { struct DisplayEvents { state: Shared, id: u32 } impl DisplayHandler {
 fn on_address_change(&self,browser:Option<&mut Browser>,frame:Option<&mut Frame>,url:Option<&CefString>) {
    if browser.as_ref().is_some_and(|b|b.is_popup()!=0) || frame.is_none_or(|f|f.is_main()==0) {return;} let url=url.map(|u|u.to_string()).unwrap_or_default();
    if !core::web_url(&url){return;}
    let address={let mut st=self.state.borrow_mut(); if let Some(t)=st.tabs.iter_mut().find(|t|t.id==self.id){t.url=url.clone();}
       st.data.add_history(url.clone(),url.clone()); save(&st); if st.active==self.id {st.address.clone()}else{None}};
    if let Some(a)=address {a.set_text(Some(&CefString::from(url.as_str())));}
 }
 fn on_title_change(&self,browser:Option<&mut Browser>,title:Option<&CefString>) {
    if browser.as_ref().is_some_and(|b|b.is_popup()!=0) {return;}
    let title=title.map(|t|t.to_string()).unwrap_or_default();
    let btn={let mut st=self.state.borrow_mut(); st.tabs.iter_mut().find(|t|t.id==self.id).map(|t|{t.title=title.clone();t.button.clone()})};
    if let Some(b)=btn {let t:String=title.chars().take(18).collect();b.set_text(Some(&CefString::from(t.as_str())));}
 }
} }
wrap_life_span_handler! { struct LifeEvents { state: Shared, id: u32 } impl LifeSpanHandler {
 fn on_after_created(&self,_browser:Option<&mut Browser>) { self.state.borrow_mut().browsers += 1; trace("on_after_created"); }
 fn do_close(&self,browser:Option<&mut Browser>)->i32 {
    trace("do_close");
    if browser.as_ref().is_some_and(|b|b.is_popup()!=0) || self.state.borrow().closing {return 0;}
    // После beforeunload снимаем с иерархии только выбранную вкладку.
    let (tab,bar,content,next)={let mut st=self.state.borrow_mut();
      let tab=st.tabs.iter().position(|t|t.id==self.id).map(|i|st.tabs.remove(i));
      (tab,st.bar.clone(),st.content.clone(),st.tabs.last().map(|t|t.id))};
    if let Some(t)=tab {if let Some(p)=bar {p.remove_child_view(Some(&mut View::from(&t.button)));} if let Some(p)=content {p.remove_child_view(Some(&mut View::from(&t.view)));} drop(t);}
    if let Some(id)=next {switch(&self.state,id);} else {make_tab(&self.state,core::HOME);}
    1
 }
 fn on_before_close(&self,browser:Option<&mut Browser>) {
    trace("on_before_close");
    let should_quit={let mut st=self.state.borrow_mut();st.browsers=st.browsers.saturating_sub(1);
      if !browser.as_ref().is_some_and(|b|b.is_popup()!=0) && st.closing {if let Some(i)=st.tabs.iter().position(|t|t.id==self.id){st.tabs.remove(i);}}
      st.window.is_none() && st.browsers==0};
    if should_quit {quit_message_loop();}
 }
} }
wrap_window_delegate! { struct MainWindow { state: Shared } impl ViewDelegate {
 fn preferred_size(&self,_unused11:Option<&mut View>)->Size {Size{width:1200,height:800}}
} impl PanelDelegate {} impl WindowDelegate {
 fn on_window_created(&self,window:Option<&mut Window>) {
    let Some(w)=window else{return}; w.set_title(Some(&CefString::from("Север — браузер")));
    let layout=w.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()})).unwrap();
    let bar=panel_create(None).unwrap(); bar.set_background_color(0xffe9f0fa); bar.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,..Default::default()}));
    let nav=panel_create(None).unwrap(); nav.set_background_color(0xfff6f9fd); let nav_layout=nav.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,..Default::default()})).unwrap();
    let extra=panel_create(None).unwrap(); extra.set_background_color(0xffe4eefb); extra.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,..Default::default()}));
    let content=panel_create(None).unwrap(); content.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,default_flex:1,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()}));
    w.add_child_view(Some(&mut View::from(&bar))); w.add_child_view(Some(&mut View::from(&nav))); w.add_child_view(Some(&mut View::from(&extra))); w.add_child_view(Some(&mut View::from(&content)));
    layout.set_flex_for_view(Some(&mut View::from(&content)),1);
    for (label,action) in [("+",Action::New),("×",Action::Close),("←",Action::Back),("→",Action::Forward),("↻",Action::Reload)] {nav.add_child_view(Some(&mut View::from(&button(label,action,&self.state))));}
    let mut delegate=Address::new(self.state.clone()); let address=textfield_create(Some(&mut delegate)).unwrap(); address.set_font_list(Some(&CefString::from("Segoe UI, 14px"))); address.set_accessible_name(Some(&CefString::from("Адресная строка"))); address.set_placeholder_text(Some(&CefString::from("Адрес или поисковый запрос")));
    nav.add_child_view(Some(&mut View::from(&address))); nav_layout.set_flex_for_view(Some(&mut View::from(&address)),1);
    nav.add_child_view(Some(&mut View::from(&button("Перейти",Action::Go,&self.state)))); nav.add_child_view(Some(&mut View::from(&button("Lite / Hard",Action::Mode,&self.state))));
    let (restore,active,hard)={let mut st=self.state.borrow_mut(); st.window=Some(w.clone());st.bar=Some(bar);st.content=Some(content);st.extra=Some(extra.clone());st.address=Some(address);
       (st.data.tabs.clone(),st.data.active,st.data.hard)};
    show_list(&self.state,true,0); extra.set_visible(hard as i32);
    for (id,key,ctrl,alt) in [(1,84,1,0),(2,87,1,0),(3,76,1,0),(4,82,1,0),(5,37,0,1),(6,39,0,1)] {w.set_accelerator(id,key,0,ctrl,alt,1);}
    let urls:Vec<_>=restore.into_iter().filter(|u|core::web_url(u)).take(15).collect();
    if urls.is_empty(){ let home=std::env::var("SEVER_START_URL").ok().filter(|u|core::web_url(u)).unwrap_or_else(||core::HOME.into()); make_tab(&self.state,&home);} else {for u in &urls {make_tab(&self.state,u);} let id=self.state.borrow().tabs.get(active.min(urls.len()-1)).map(|t|t.id).unwrap();switch(&self.state,id);}
    w.show();
 }
 fn can_resize(&self,_unused12:Option<&mut Window>)->i32{1} fn can_maximize(&self,_unused13:Option<&mut Window>)->i32{1} fn can_minimize(&self,_unused14:Option<&mut Window>)->i32{1}
 fn can_close(&self,_unused15:Option<&mut Window>)->i32 {
    trace("can_close");
    let hosts={let mut st=self.state.borrow_mut(); if !st.closing {save(&st);st.closing=true;} st.tabs.iter().filter_map(|t|t.view.browser()).filter_map(|b|b.host()).collect::<Vec<_>>()};
    for h in hosts {if h.try_close_browser()==0 {return 0;}}
    1
 }
 fn on_window_destroyed(&self,_window:Option<&mut Window>){trace("on_window_destroyed");let (tabs,quit)={let mut st=self.state.borrow_mut();st.window=None;(std::mem::take(&mut st.tabs),st.browsers==0)}; drop(tabs); if quit {quit_message_loop();}}
 fn on_accelerator(&self,_unused17:Option<&mut Window>,id:i32)->i32 {match id {1=>act(&self.state,Action::New),2=>act(&self.state,Action::Close),3=>{if let Some(a)=self.state.borrow().address.clone(){a.request_focus();a.select_all(0);}},4=>act(&self.state,Action::Reload),5=>act(&self.state,Action::Back),6=>act(&self.state,Action::Forward),_=>return 0};1}
 fn window_runtime_style(&self)->RuntimeStyle{RuntimeStyle::ALLOY}
} }
wrap_app! { struct AppImpl; impl App {fn browser_process_handler(&self)->Option<BrowserProcessHandler>{Some(Process::new())}} }
wrap_browser_process_handler! {struct Process; impl BrowserProcessHandler {
 fn on_context_initialized(&self){let state=Rc::new(RefCell::new(BrowserState{data:read_data(),tabs:vec![],active:0,next:1,window:None,bar:None,content:None,extra:None,address:None,closing:false,browsers:0})); let mut d=MainWindow::new(state);window_create_top_level(Some(&mut d));}
} }
#[unsafe(no_mangle)]
unsafe extern "C" fn RunWinMain(
    instance: sys::HINSTANCE,
    _cmd: *const u8,
    _show: i32,
    sandbox_info: *mut u8,
) -> i32 {
    let _ = api_hash(sys::CEF_API_VERSION_LAST, 0);
    let args = args::Args::from(MainArgs { instance });
    let process = execute_process(Some(args.as_main_args()), None, sandbox_info);
    if process >= 0 {
        return process;
    }
    let dir = profile();
    if std::fs::create_dir_all(&dir).is_err() {
        return 1;
    }
    let cache = dir.join("cef").to_string_lossy().into_owned();
    let settings = Settings {
        no_sandbox: 0,
        command_line_args_disabled: 1,
        root_cache_path: CefString::from(cache.as_str()),
        cache_path: CefString::from(cache.as_str()),
        persist_session_cookies: 1,
        locale: CefString::from("ru"),
        accept_language_list: CefString::from("ru-RU,ru,en-US,en"),
        use_views_default_popup: 1,
        ..Default::default()
    };
    let mut app = AppImpl::new();
    if initialize(
        Some(args.as_main_args()),
        Some(&settings),
        Some(&mut app),
        sandbox_info,
    ) != 1
    {
        return 1;
    }
    run_message_loop();
    shutdown();
    0
}
