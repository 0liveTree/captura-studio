#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use eframe::egui;

fn main() -> eframe::Result<()> {
    let opts = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default().with_inner_size([700.0, 700.0]),
        ..Default::default()
    };
    eframe::run_native("teste", opts, Box::new(|_cc| Ok(Box::new(App::default()))))
}

#[derive(Default)]
struct App {
    n: u32,
}

impl eframe::App for App {
    fn update(&mut self, ctx: &egui::Context, _f: &mut eframe::Frame) {
        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("Ola");
            if ui.button("clique").clicked() {
                self.n += 1;
            }
            ui.label(format!("{}", self.n));
        });
    }
}
