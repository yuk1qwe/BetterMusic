#[cfg(windows)]
fn main() {
    let out = std::path::Path::new("dist/Feather");
    std::fs::create_dir_all(out).unwrap();
    cef::build_util::win::build_bundle(out, "feather", true).unwrap();
}
#[cfg(not(windows))]
fn main() {
    eprintln!("Сборка доступна на Windows");
}
