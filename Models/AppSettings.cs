namespace ArchiveThumbViewer.Models
{
    public class AppSettings
    {
        public string RootPath { get; set; } = "";     // 마지막 사용 경로
        public string ViewerPath { get; set; } = "";   // 외부 뷰어 경로

        // 창 배치(이미 추가되어 있다면 유지)
        public double WindowWidth { get; set; } = 1180;
        public double WindowHeight { get; set; } = 720;
        public double? WindowTop { get; set; } = null;
        public double? WindowLeft { get; set; } = null;
        public string WindowState { get; set; } = "Normal";

        // ▼ 썸네일 스케일 저장
        public double ThumbnailScale { get; set; } = 1.0; // 0.6 ~ 2.0 권장
    }
}
