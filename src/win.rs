use crate::core::{self, Data, Entry};
use cef::*;
use std::{cell::RefCell, path::PathBuf, rc::Rc, sync::OnceLock};

// Интерфейс реализован нативными CEF Views. Страницы не получают канал к Rust.
type Shared = Rc<RefCell<BrowserState>>;
struct Tab {
    id: u32,
    view: Option<BrowserView>,
    button: LabelButton,
    url: String,
    title: String,
}
#[derive(Clone)]
struct DownloadRow {
    id: u32,
    name: String,
    status: String,
}
struct BrowserState {
    data: Data,
    downloads: Vec<DownloadRow>,
    tabs: Vec<Tab>,
    active: u32,
    next: u32,
    window: Option<Window>,
    bar: Option<Panel>,
    content: Option<Panel>,
    extra: Option<Panel>,
    rail: Option<Panel>,
    address: Option<Textfield>,
    mode: Option<LabelButton>,
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
const BG: u32 = 0xff11111c;
const SURFACE: u32 = 0xff191827;
const CARD: u32 = 0xff222033;
const ACCENT: u32 = 0xff443267;
const TEXT: u32 = 0xffeeeaf9;
const MUTED: u32 = 0xffaaa4bf;
const POPUP_ADDRESS_ID: i32 = 17001;

fn start_page_url() -> &'static str {
    static START: OnceLock<String> = OnceLock::new();
    START.get_or_init(|| {
        let image = CefString::from(&base64_encode(Some(include_bytes!(
            "../assets/new_tab.webp"
        ))))
        .to_string();
        let page = include_str!("../assets/new_tab.html").replace("{{BACKGROUND}}", &image);
        let encoded = CefString::from(&base64_encode(Some(page.as_bytes()))).to_string();
        format!("data:text/html;charset=utf-8;base64,{encoded}")
    })
}
fn load_url(url: &str) -> &str {
    if url == core::HOME {
        start_page_url()
    } else {
        url
    }
}
fn shown_address(url: &str) -> String {
    core::display_address(url, url == core::HOME || url == start_page_url())
}
fn save(s: &BrowserState) {
    if s.closing {
        return;
    }
    let mut data = s.data.clone();
    data.tabs = s
        .tabs
        .iter()
        .take(core::MAX_RESTORED_TABS)
        .map(|t| core::restorable(&t.url))
        .collect();
    data.active = s
        .tabs
        .iter()
        .position(|t| t.id == s.active)
        .unwrap_or(0)
        .min(data.tabs.len().saturating_sub(1));
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
        .and_then(|t| t.view.as_ref())
        .and_then(|v| v.browser())
}
fn ensure_tab(s: &Shared, id: u32) {
    trace("ensure_tab:start");
    let (url, content) = {
        let st = s.borrow();
        let Some(t) = st.tabs.iter().find(|t| t.id == id) else {
            return;
        };
        if t.view.is_some() {
            return;
        }
        (t.url.clone(), st.content.clone().unwrap())
    };
    let mut client = BrowserClient::new(s.clone(), id);
    let mut delegate = BrowserViewEvents::new(s.clone());
    let view = browser_view_create(
        Some(&mut client),
        Some(&CefString::from(load_url(&url))),
        Some(&BrowserSettings::default()),
        None,
        None,
        Some(&mut delegate),
    )
    .expect("вкладка");
    view.set_visible(0);
    {
        let mut st = s.borrow_mut();
        if let Some(t) = st.tabs.iter_mut().find(|t| t.id == id) {
            t.view = Some(view.clone());
        } else {
            return;
        }
    }
    content.add_child_view(Some(&mut View::from(&view)));
}
fn switch(s: &Shared, id: u32) {
    trace("switch:start");
    {
        let mut st = s.borrow_mut();
        if !st.tabs.iter().any(|t| t.id == id) {
            return;
        }
        st.active = id;
    }
    ensure_tab(s, id);
    let (views, address, url, window, content) = {
        let st = s.borrow();
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
        (
            views,
            st.address.clone(),
            url,
            st.window.clone(),
            st.content.clone(),
        )
    };
    for (view, button, active) in views {
        if let Some(view) = view {
            view.set_visible(active as i32);
        }
        button.set_background_color(if active { ACCENT } else { SURFACE });
        button.set_enabled_text_colors(if active { TEXT } else { MUTED });
    }
    if let Some(a) = address {
        a.set_text(Some(&CefString::from(shown_address(&url).as_str())));
    }
    if let Some(w) = window {
        w.invalidate_layout();
        w.layout();
    }
    if let Some(p) = content.as_ref() {
        p.invalidate_layout();
        p.layout();
    }
    if std::env::var_os("SEVER_LAYOUT_DEBUG").is_some() {
        let view = {
            let st = s.borrow();
            st.tabs
                .iter()
                .find(|t| t.id == id)
                .and_then(|t| t.view.clone())
        };
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
    button.set_enabled_text_colors(TEXT);
    button.set_background_color(SURFACE);
    button.set_minimum_size(Some(&Size {
        width: 0,
        height: 38,
    }));
    button
}
fn text_label(text: &str, color: u32) -> LabelButton {
    let mut delegate = PassiveLabel::new();
    let button =
        label_button_create(Some(&mut delegate), Some(&CefString::from(text))).expect("подпись");
    button.set_focusable(0);
    button.set_font_list(Some(&CefString::from("Segoe UI, 13px")));
    button.set_enabled_text_colors(color);
    button.set_background_color(SURFACE);
    button.set_horizontal_alignment(HorizontalAlignment::LEFT);
    button.set_minimum_size(Some(&Size {
        width: 0,
        height: 32,
    }));
    button
}
fn add_tab(s: &Shared, url: &str) -> u32 {
    let (id, bar) = {
        let mut st = s.borrow_mut();
        let id = st.next;
        st.next += 1;
        (id, st.bar.clone().unwrap())
    };
    let btn = button(&core::tab_caption(url), Action::Tab(id), s);
    btn.set_maximum_size(Some(&Size {
        width: 175,
        height: 40,
    }));
    btn.set_tooltip_text(Some(&CefString::from(shown_address(url).as_str())));
    {
        let mut st = s.borrow_mut();
        st.tabs.push(Tab {
            id,
            view: None,
            button: btn.clone(),
            url: url.into(),
            title: core::tab_caption(url),
        });
    }
    bar.add_child_view(Some(&mut View::from(&btn)));
    id
}
fn make_tab(s: &Shared, url: &str) {
    let id = add_tab(s, url);
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
    Home,
    Resources,
    Downloads,
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
        Action::Home => {
            if let Some(b) = current(s) {
                if let Some(frame) = b.main_frame() {
                    frame.load_url(Some(&CefString::from(start_page_url())));
                }
            }
        }
        Action::Resources => show_resources(s),
        Action::Downloads => show_downloads(s),
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
            let (extra, hard, mode) = {
                let mut st = s.borrow_mut();
                st.data.hard = !st.data.hard;
                (st.extra.clone(), st.data.hard, st.mode.clone())
            };
            if let Some(mode) = mode {
                mode.set_text(Some(&CefString::from(if hard {
                    "Режим: Hard"
                } else {
                    "Режим: Lite"
                })));
            }
            let rail = { s.borrow().rail.clone() };
            if let Some(p) = rail {
                p.set_visible(hard as i32);
            }
            if let Some(p) = extra {
                if !hard {
                    p.remove_all_child_views();
                }
                p.set_visible(hard as i32);
            }
            if hard {
                show_resources(s);
            }
            let window = { s.borrow().window.clone() };
            if let Some(w) = window {
                w.invalidate_layout();
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
            drop(st);
            let hard = { s.borrow().data.hard };
            if hard {
                show_list(s, true, 0);
            }
        }
        Action::Bookmarks => show_list(s, true, 0),
        Action::History => show_list(s, false, 0),
        Action::ListPage(bookmarks, page) => show_list(s, bookmarks, page),
        Action::Open(url) => navigate(s, &url),
        Action::External => external(s),
    }
}
fn panel_chrome(panel: &Panel, s: &Shared, title: &str) {
    panel.remove_all_child_views();
    panel.add_child_view(Some(&mut View::from(&text_label(
        "✦   FEATHER",
        0xffb99bff,
    ))));
    panel.add_child_view(Some(&mut View::from(&text_label(title, TEXT))));
    let row = panel_create(None).unwrap();
    row.set_background_color(SURFACE);
    row.set_to_box_layout(Some(&BoxLayoutSettings {
        horizontal: 1,
        between_child_spacing: 3,
        cross_axis_alignment: AxisAlignment::STRETCH,
        ..Default::default()
    }));
    for (caption, action) in [
        ("Закладки", Action::Bookmarks),
        ("История", Action::History),
        ("Ресурсы", Action::Resources),
    ] {
        let b = button(caption, action, s);
        b.set_font_list(Some(&CefString::from("Segoe UI, 12px")));
        row.add_child_view(Some(&mut View::from(&b)));
    }
    panel.add_child_view(Some(&mut View::from(&row)));
}
fn refresh_panel(s: &Shared) {
    let w = { s.borrow().window.clone() };
    if let Some(w) = w {
        w.invalidate_layout();
        w.layout();
    }
}
fn show_resources(s: &Shared) {
    let (panel, hard, tabs, deferred) = {
        let st = s.borrow();
        (
            st.extra.clone(),
            st.data.hard,
            st.tabs.len(),
            st.tabs.iter().filter(|t| t.view.is_none()).count(),
        )
    };
    let Some(panel) = panel else { return };
    if !hard {
        return;
    }
    panel_chrome(&panel, s, "Менеджер ресурсов");
    if let Some(snap) = crate::metrics::memory_snapshot() {
        let label = format!(
            "Оперативная память: {:.0} МиБ",
            snap.bytes as f64 / 1048576.0
        );
        panel.add_child_view(Some(&mut View::from(&text_label(&label, TEXT))));
        let detail = format!("Процессы: {} из {}", snap.measured, snap.total);
        panel.add_child_view(Some(&mut View::from(&text_label(&detail, MUTED))));
        if snap.measured < snap.total {
            panel.add_child_view(Some(&mut View::from(&text_label(
                "Часть процессов недоступна",
                MUTED,
            ))));
        }
    } else {
        panel.add_child_view(Some(&mut View::from(&text_label(
            "Память недоступна",
            MUTED,
        ))));
    }
    panel.add_child_view(Some(&mut View::from(&text_label(
        &format!("Вкладки: {tabs}  ·  не загружены: {deferred}"),
        MUTED,
    ))));
    panel.add_child_view(Some(&mut View::from(&text_label(
        "Обновляется по нажатию",
        MUTED,
    ))));
    panel.add_child_view(Some(&mut View::from(&button(
        "↻  Обновить",
        Action::Resources,
        s,
    ))));
    panel.add_child_view(Some(&mut View::from(&button(
        "Открыть в системе ↗",
        Action::External,
        s,
    ))));
    refresh_panel(s);
}
fn show_downloads(s: &Shared) {
    let (panel, hard, items) = {
        let st = s.borrow();
        (st.extra.clone(), st.data.hard, st.downloads.clone())
    };
    let Some(panel) = panel else { return };
    if !hard {
        return;
    }
    panel_chrome(&panel, s, "Загрузки");
    if items.is_empty() {
        panel.add_child_view(Some(&mut View::from(&text_label(
            "Пока нет загрузок",
            MUTED,
        ))));
    }
    for item in items.into_iter().take(6) {
        let label = format!(
            "{} · {}",
            item.name.chars().take(19).collect::<String>(),
            item.status
        );
        panel.add_child_view(Some(&mut View::from(&text_label(&label, TEXT))));
    }
    panel.add_child_view(Some(&mut View::from(&text_label(
        "Список текущего запуска",
        MUTED,
    ))));
    panel.add_child_view(Some(&mut View::from(&button(
        "↻  Обновить",
        Action::Downloads,
        s,
    ))));
    refresh_panel(s);
}
fn show_list(s: &Shared, bookmarks: bool, page: usize) {
    let (panel, items, hard) = {
        let st = s.borrow();
        (
            st.extra.clone(),
            if bookmarks {
                st.data.bookmarks.clone()
            } else {
                st.data.history.clone()
            },
            st.data.hard,
        )
    };
    let Some(panel) = panel else { return };
    if !hard {
        return;
    }
    panel_chrome(
        &panel,
        s,
        if bookmarks {
            "Закладки"
        } else {
            "История"
        },
    );
    if bookmarks {
        panel.add_child_view(Some(&mut View::from(&button(
            "★ Сохранить / убрать",
            Action::Bookmark,
            s,
        ))));
    }
    if items.is_empty() {
        panel.add_child_view(Some(&mut View::from(&text_label(
            if bookmarks {
                "Пока нет закладок"
            } else {
                "История пока пуста"
            },
            MUTED,
        ))));
    }
    let pages = items.len().div_ceil(6);
    let page = page.min(pages.saturating_sub(1));
    for e in items.into_iter().skip(page * 6).take(6) {
        let title = if e.title.is_empty() {
            e.url.clone()
        } else {
            e.title.clone()
        };
        let label: String = title.chars().take(26).collect();
        let b = button(&label, Action::Open(e.url.clone()), s);
        b.set_horizontal_alignment(HorizontalAlignment::LEFT);
        b.set_tooltip_text(Some(&CefString::from(e.url.as_str())));
        panel.add_child_view(Some(&mut View::from(&b)));
    }
    let nav = panel_create(None).unwrap();
    nav.set_background_color(SURFACE);
    nav.set_to_box_layout(Some(&BoxLayoutSettings {
        horizontal: 1,
        ..Default::default()
    }));
    if page > 0 {
        nav.add_child_view(Some(&mut View::from(&button(
            "← Назад",
            Action::ListPage(bookmarks, page - 1),
            s,
        ))));
    }
    if page + 1 < pages {
        nav.add_child_view(Some(&mut View::from(&button(
            "Ещё →",
            Action::ListPage(bookmarks, page + 1),
            s,
        ))));
    }
    panel.add_child_view(Some(&mut View::from(&nav)));
    panel.add_child_view(Some(&mut View::from(&button(
        "Открыть в системе ↗",
        Action::External,
        s,
    ))));
    refresh_panel(s);
}
wrap_button_delegate! { struct PassiveLabel {} impl ViewDelegate {} impl ButtonDelegate {} }
// Обновление панели может удалить нажатую кнопку. Выполняем действие в
// следующем сообщении UI, когда CEF уже вернулся из обработчика этой кнопки.
wrap_task! { struct ButtonAction { state: Shared, action: Action }
impl Task { fn execute(&self) { act(&self.state, self.action.clone()); } } }
wrap_button_delegate! { struct Buttons { state: Shared, action: Action }
impl ViewDelegate {} impl ButtonDelegate { fn on_button_pressed(&self,_unused1:Option<&mut Button>){
    let mut task = ButtonAction::new(self.state.clone(), self.action.clone());
    post_task(ThreadId::UI, Some(&mut task));
} } }
wrap_textfield_delegate! { struct Address { state: Shared }
impl ViewDelegate {} impl TextfieldDelegate { fn on_key_event(&self,_unused2:Option<&mut Textfield>,event:Option<&KeyEvent>)->i32 {
   if event.is_some_and(|e|e.type_==KeyEventType::RAWKEYDOWN && e.windows_key_code==13) {act(&self.state,Action::Go);1}else{0}
} } }
wrap_browser_view_delegate! { struct BrowserViewEvents { state: Shared } impl ViewDelegate {} impl BrowserViewDelegate {
 fn browser_runtime_style(&self)->RuntimeStyle {RuntimeStyle::ALLOY}
 fn delegate_for_popup_browser_view(&self,_parent:Option<&mut BrowserView>,_settings:Option<&BrowserSettings>,_client:Option<&mut Client>,_devtools:i32)->Option<BrowserViewDelegate> {
    Some(BrowserViewEvents::new(self.state.clone()))
 }
 fn on_popup_browser_view_created(&self,_parent:Option<&mut BrowserView>,popup:Option<&mut BrowserView>,_devtools:i32)->i32 {
    let Some(popup)=popup else{return 0};
    let mut delegate=PopupWindow::new(RefCell::new(Some(popup.clone())));
    window_create_top_level(Some(&mut delegate));1
 }
} }
wrap_window_delegate! { struct PopupWindow { view: RefCell<Option<BrowserView>> }
 impl ViewDelegate {fn preferred_size(&self,_view:Option<&mut View>)->Size {Size{width:850,height:650}}}
 impl PanelDelegate {} impl WindowDelegate {
 fn on_window_created(&self,window:Option<&mut Window>){
    let Some(w)=window else{return};w.set_title(Some(&CefString::from("Feather — всплывающее окно")));
    let layout=w.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()})).unwrap();
    let address=textfield_create(None).unwrap();address.set_read_only(1);address.set_id(POPUP_ADDRESS_ID);
    address.set_accessible_name(Some(&CefString::from("Адрес всплывающего окна")));
    let url=self.view.borrow().as_ref().and_then(|v|v.browser()).and_then(|b|b.main_frame()).map(|f|CefString::from(&f.url()).to_string()).unwrap_or_else(||"about:blank".into());
    address.set_text(Some(&CefString::from(shown_address(&url).as_str())));
    w.add_child_view(Some(&mut View::from(&address)));
    if let Some(v)=self.view.borrow().as_ref(){w.add_child_view(Some(&mut View::from(v)));layout.set_flex_for_view(Some(&mut View::from(v)),1);}
    w.show();
 }
 fn can_resize(&self,_w:Option<&mut Window>)->i32{1}
 fn can_maximize(&self,_w:Option<&mut Window>)->i32{1}
 fn can_minimize(&self,_w:Option<&mut Window>)->i32{1}
 fn can_close(&self,_w:Option<&mut Window>)->i32{self.view.borrow().as_ref().and_then(|v|v.browser()).and_then(|b|b.host()).map_or(1,|h|h.try_close_browser())}
 fn on_window_destroyed(&self,_w:Option<&mut Window>){self.view.borrow_mut().take();}
 fn window_runtime_style(&self)->RuntimeStyle{RuntimeStyle::ALLOY}
} }
wrap_client! { struct BrowserClient { state: Shared, id: u32 } impl Client {
 fn display_handler(&self)->Option<DisplayHandler>{Some(DisplayEvents::new(self.state.clone(),self.id))}
 fn life_span_handler(&self)->Option<LifeSpanHandler>{Some(LifeEvents::new(self.state.clone(),self.id))}
 fn download_handler(&self)->Option<DownloadHandler>{Some(Downloads::new(self.state.clone()))}
} }
wrap_download_handler! { struct Downloads { state: Shared } impl DownloadHandler {
 fn can_download(&self,_unused3:Option<&mut Browser>,_unused4:Option<&CefString>,_unused5:Option<&CefString>)->i32{1}
 fn on_before_download(&self,_unused6:Option<&mut Browser>,_unused7:Option<&mut DownloadItem>,_unused8:Option<&CefString>,callback:Option<&mut BeforeDownloadCallback>)->i32 { if let Some(cb)=callback {cb.cont(None,1);1}else{0} }
 fn on_download_updated(&self,_browser:Option<&mut Browser>,item:Option<&mut DownloadItem>,_callback:Option<&mut DownloadItemCallback>) {
    let Some(item)=item else{return};if item.is_valid()==0{return;}
    let mut name=CefString::from(&item.suggested_file_name()).to_string();if name.is_empty(){name="Файл".into();}
    let status=if item.is_complete()!=0 {"готово".into()} else if item.is_canceled()!=0 {"отменено".into()} else if item.is_interrupted()!=0 {"ошибка".into()} else if item.percent_complete()>=0 {format!("{}%",item.percent_complete())} else {"загрузка".into()};
    let row=DownloadRow{id:item.id(),name,status};let mut st=self.state.borrow_mut();
    if let Some(pos)=st.downloads.iter().position(|r|r.id==row.id){st.downloads.remove(pos);}st.downloads.insert(0,row);st.downloads.truncate(50);
 }
} }
wrap_display_handler! { struct DisplayEvents { state: Shared, id: u32 } impl DisplayHandler {
 fn on_address_change(&self,browser:Option<&mut Browser>,frame:Option<&mut Frame>,url:Option<&CefString>) {
    let mut browser=browser;
    if frame.is_none_or(|f|f.is_main()==0){return;}
    let raw=url.map(|u|u.to_string()).unwrap_or_default();
    if browser.as_ref().is_some_and(|b|b.is_popup()!=0) {
       if let Some(view)=browser_view_get_for_browser(browser.as_deref_mut()) {
          if let Some(window)=view.window() {if let Some(address)=window.view_for_id(POPUP_ADDRESS_ID).and_then(|v|v.as_textfield()) {
             address.set_text(Some(&CefString::from(shown_address(&raw).as_str())));
          }}
       }
       return;
    }
    let canonical=if raw==start_page_url(){core::HOME.to_string()} else {raw.clone()};
    let (address,tab_button)={let mut st=self.state.borrow_mut();
       let mut changed=false;
       let tab_button=st.tabs.iter_mut().find(|t|t.id==self.id).map(|t|{changed=t.url!=canonical;t.url=canonical.clone();t.button.clone()});
       if changed && core::web_url(&canonical){st.data.add_history(canonical.clone(),canonical.clone());}
       if changed {save(&st);} (if st.active==self.id {st.address.clone()}else{None},tab_button)};
    let shown=shown_address(&canonical);
    if let Some(b)=tab_button {b.set_tooltip_text(Some(&CefString::from(shown.as_str())));}
    if let Some(a)=address {a.set_text(Some(&CefString::from(shown.as_str())));}
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
    if let Some(t)=tab {if let Some(p)=bar {p.remove_child_view(Some(&mut View::from(&t.button)));} if let (Some(p),Some(v))=(content,t.view.as_ref()) {p.remove_child_view(Some(&mut View::from(v)));} drop(t);}
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
wrap_panel_delegate! { struct FixedPanel { width: i32 } impl ViewDelegate {
 fn preferred_size(&self,_view:Option<&mut View>)->Size {Size{width:self.width,height:0}}
} impl PanelDelegate {} }
wrap_window_delegate! { struct MainWindow { state: Shared } impl ViewDelegate {
 fn preferred_size(&self,_unused11:Option<&mut View>)->Size {Size{width:1200,height:800}}
} impl PanelDelegate {} impl WindowDelegate {
 fn on_window_created(&self,window:Option<&mut Window>) {
    trace("window:created");let Some(w)=window else{return};w.set_title(Some(&CefString::from("Feather — браузер")));w.set_background_color(BG);
    if let Some(mut icon)=image_create(){if icon.add_png(1.0,Some(include_bytes!("../assets/feather-icon.png")))!=0 {w.set_window_icon(Some(&mut icon));w.set_window_app_icon(Some(&mut icon));}}
    let root=w.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()})).unwrap();
    let top=panel_create(None).unwrap();top.set_background_color(SURFACE);
    let top_layout=top.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,between_child_spacing:5,cross_axis_alignment:AxisAlignment::STRETCH,inside_border_insets:Insets{top:6,left:8,bottom:4,right:8},..Default::default()})).unwrap();
    let brand=button("✦  Feather",Action::Home,&self.state);brand.set_enabled_text_colors(0xffdbcaff);brand.set_tooltip_text(Some(&CefString::from("Новая вкладка Feather")));
    top.add_child_view(Some(&mut View::from(&brand)));
    let bar=panel_create(None).unwrap();bar.set_background_color(SURFACE);bar.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,between_child_spacing:4,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()}));
    top.add_child_view(Some(&mut View::from(&bar)));top_layout.set_flex_for_view(Some(&mut View::from(&bar)),1);
    for (name,tip,action) in [("+","Новая вкладка · Ctrl+T",Action::New),("×","Закрыть вкладку · Ctrl+W",Action::Close)] {
       let b=button(name,action,&self.state);b.set_tooltip_text(Some(&CefString::from(tip)));top.add_child_view(Some(&mut View::from(&b)));
    }
    let nav=panel_create(None).unwrap();nav.set_background_color(BG);
    let nav_layout=nav.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,between_child_spacing:4,cross_axis_alignment:AxisAlignment::STRETCH,inside_border_insets:Insets{top:4,left:10,bottom:7,right:10},..Default::default()})).unwrap();
    for (name,tip,action) in [("←","Назад · Alt+←",Action::Back),("→","Вперёд · Alt+→",Action::Forward),("↻","Обновить · Ctrl+R",Action::Reload)] {
       let b=button(name,action,&self.state);b.set_background_color(BG);b.set_tooltip_text(Some(&CefString::from(tip)));nav.add_child_view(Some(&mut View::from(&b)));
    }
    let mut delegate=Address::new(self.state.clone());let address=textfield_create(Some(&mut delegate)).unwrap();
    address.set_font_list(Some(&CefString::from("Segoe UI, 14px")));address.set_accessible_name(Some(&CefString::from("Адресная строка")));
    address.set_placeholder_text(Some(&CefString::from("Введите запрос или адрес")));address.set_background_color(CARD);
    nav.add_child_view(Some(&mut View::from(&address)));nav_layout.set_flex_for_view(Some(&mut View::from(&address)),1);
    nav.add_child_view(Some(&mut View::from(&button("Перейти",Action::Go,&self.state))));
    let fav=button("☆",Action::Bookmark,&self.state);fav.set_tooltip_text(Some(&CefString::from("Добавить или убрать закладку")));nav.add_child_view(Some(&mut View::from(&fav)));
    let mode=button(if self.state.borrow().data.hard {"Режим: Hard"} else {"Режим: Lite"},Action::Mode,&self.state);mode.set_tooltip_text(Some(&CefString::from("Переключить режим и боковые панели")));nav.add_child_view(Some(&mut View::from(&mode)));
    let body=panel_create(None).unwrap();body.set_background_color(BG);
    let body_layout=body.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:1,between_child_spacing:5,cross_axis_alignment:AxisAlignment::STRETCH,inside_border_insets:Insets{top:0,left:6,bottom:6,right:6},..Default::default()})).unwrap();
    let mut rail_delegate=FixedPanel::new(48);let rail=panel_create(Some(&mut rail_delegate)).unwrap();rail.set_background_color(SURFACE);
    rail.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,between_child_spacing:5,cross_axis_alignment:AxisAlignment::STRETCH,inside_border_insets:Insets{top:10,left:4,bottom:4,right:4},..Default::default()}));
    for (name,tip,action) in [("⌂","Стартовая страница",Action::Home),("☆","Закладки",Action::Bookmarks),("◷","История",Action::History),("↓","Загрузки",Action::Downloads),("◉","Ресурсы",Action::Resources),("↗","Открыть в системе",Action::External)] {
       let b=button(name,action,&self.state);b.set_accessible_name(Some(&CefString::from(tip)));b.set_tooltip_text(Some(&CefString::from(tip)));rail.add_child_view(Some(&mut View::from(&b)));
    }
    let content=panel_create(None).unwrap();content.set_background_color(BG);
    content.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,default_flex:1,cross_axis_alignment:AxisAlignment::STRETCH,..Default::default()}));
    let mut side_delegate=FixedPanel::new(260);let extra=panel_create(Some(&mut side_delegate)).unwrap();extra.set_background_color(SURFACE);
    extra.set_to_box_layout(Some(&BoxLayoutSettings{horizontal:0,between_child_spacing:3,cross_axis_alignment:AxisAlignment::STRETCH,inside_border_insets:Insets{top:12,left:10,bottom:8,right:10},..Default::default()}));
    body.add_child_view(Some(&mut View::from(&rail)));body.add_child_view(Some(&mut View::from(&content)));body.add_child_view(Some(&mut View::from(&extra)));
    body_layout.set_flex_for_view(Some(&mut View::from(&content)),1);
    w.add_child_view(Some(&mut View::from(&top)));w.add_child_view(Some(&mut View::from(&nav)));w.add_child_view(Some(&mut View::from(&body)));
    root.set_flex_for_view(Some(&mut View::from(&body)),1);
    trace("window:views_ready");let (restore,active,hard)={let mut st=self.state.borrow_mut();st.window=Some(w.clone());st.bar=Some(bar);st.content=Some(content);st.extra=Some(extra.clone());st.rail=Some(rail.clone());st.address=Some(address);st.mode=Some(mode);
       (st.data.tabs.clone(),st.data.active,st.data.hard)};
    rail.set_visible(hard as i32);extra.set_visible(hard as i32);
    for (id,key,ctrl,alt) in [(1,84,1,0),(2,87,1,0),(3,76,1,0),(4,82,1,0),(5,37,0,1),(6,39,0,1)] {w.set_accelerator(id,key,0,ctrl,alt,1);}
    let urls:Vec<_>=restore.iter().take(core::MAX_RESTORED_TABS).map(|u|core::restorable(u)).collect();
    if urls.is_empty(){let home=std::env::var("SEVER_START_URL").ok().filter(|u|core::web_url(u)).unwrap_or_else(||core::HOME.into());make_tab(&self.state,&home);}
    else {
      let ids=urls.iter().map(|u|add_tab(&self.state,u)).collect::<Vec<_>>();
      if std::env::var_os("FEATHER_EAGER_RESTORE").is_some(){for id in &ids {ensure_tab(&self.state,*id);}}
      switch(&self.state,ids[active.min(ids.len()-1)]);
    }
    trace("window:tabs_ready");if hard {show_resources(&self.state);}
    trace("window:show");w.show();
 }
 fn can_resize(&self,_unused12:Option<&mut Window>)->i32{1} fn can_maximize(&self,_unused13:Option<&mut Window>)->i32{1} fn can_minimize(&self,_unused14:Option<&mut Window>)->i32{1}
 fn can_close(&self,_unused15:Option<&mut Window>)->i32 {
    trace("can_close");
    let hosts={let mut st=self.state.borrow_mut(); if !st.closing {save(&st);st.closing=true;} st.tabs.iter().filter_map(|t|t.view.as_ref().and_then(|v|v.browser())).filter_map(|b|b.host()).collect::<Vec<_>>()};
    for h in hosts {if h.try_close_browser()==0 {return 0;}}
    1
 }
 fn on_window_destroyed(&self,_window:Option<&mut Window>){trace("on_window_destroyed");let (tabs,quit)={let mut st=self.state.borrow_mut();st.window=None;(std::mem::take(&mut st.tabs),st.browsers==0)}; drop(tabs); if quit {quit_message_loop();}}
 fn on_accelerator(&self,_unused17:Option<&mut Window>,id:i32)->i32 {match id {1=>act(&self.state,Action::New),2=>act(&self.state,Action::Close),3=>{if let Some(a)=self.state.borrow().address.clone(){a.request_focus();a.select_all(0);}},4=>act(&self.state,Action::Reload),5=>act(&self.state,Action::Back),6=>act(&self.state,Action::Forward),_=>return 0};1}
 fn window_runtime_style(&self)->RuntimeStyle{RuntimeStyle::ALLOY}
} }
wrap_app! { struct AppImpl; impl App {
 fn on_before_command_line_processing(&self,process_type:Option<&CefString>,command_line:Option<&mut CommandLine>){
    if process_type.is_none_or(|t|t.to_string().is_empty()){if let Some(c)=command_line{c.append_switch(Some(&CefString::from("force-dark-mode")));}}
 }
 fn browser_process_handler(&self)->Option<BrowserProcessHandler>{Some(Process::new())}
} }
wrap_browser_process_handler! {struct Process; impl BrowserProcessHandler {
 fn on_context_initialized(&self){trace("context_initialized");let state=Rc::new(RefCell::new(BrowserState{data:read_data(),downloads:vec![],tabs:vec![],active:0,next:1,window:None,bar:None,content:None,extra:None,rail:None,address:None,mode:None,closing:false,browsers:0})); let mut d=MainWindow::new(state);window_create_top_level(Some(&mut d));}
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
    if std::env::var_os("SEVER_LAYOUT_DEBUG").is_some() {
        let panic_path = dir.join("panic.txt");
        std::panic::set_hook(Box::new(move |info| {
            let _ = std::fs::write(
                &panic_path,
                format!("{info}\n{}", std::backtrace::Backtrace::force_capture()),
            );
        }));
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
