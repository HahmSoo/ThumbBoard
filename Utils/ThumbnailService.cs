using SharpCompress.Archives;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageMagick; // Magick.NET

namespace ArchiveThumbViewer.Utils
{
    public static class ThumbnailService
    {
        // 썸네일 캐시 루트
        public static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArchiveThumbViewer", "thumbcache");

        // 지원 이미지 확장자
        private static readonly HashSet<string> ImgExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg",".jpeg",".png",".webp",".bmp",".gif",".tif",".tiff" };

        // UI 카드 기본 260x360 → 품질 여유를 위해 2x (확대 시에도 깨짐 최소화)
        private const int TargetW = 510;
        private const int TargetH = 720;

        // 캐시 버전(썸네일 방식 변경 시 반드시 바꿔야 이전 캐시와 구분됨)
        private const string CacheVersion = "v2_fit_510x720_bg1A1D23_q85";

        static ThumbnailService()
        {
            Directory.CreateDirectory(CacheDir);
        }

        /// <summary>
        /// 아카이브 첫 이미지로부터 '맞춤(Fit, 레터박스)' 썸네일 생성/로드
        /// </summary>
        public static async Task<BitmapImage?> GetThumbnailAsync(string archivePath, CancellationToken ct)
        {
            try
            {
                var fi = new FileInfo(archivePath);
                if (!fi.Exists) return null;

                // 캐시 키: 파일 경로 + mtime + 버전
                string key = $"{CacheVersion}|{archivePath}|{fi.LastWriteTimeUtc.Ticks}";
                string hash = ComputeSHA1(key);
                string cachedPath = Path.Combine(CacheDir, hash + ".jpg");

                if (File.Exists(cachedPath))
                    return LoadBitmapFromFile(cachedPath);

                // 캐시 없음 → 첫 이미지 추출
                using var ms = await ExtractFirstImageAsync(archivePath, ct);
                if (ms == null) return null;

                ms.Position = 0;
                using var img = new MagickImage(ms);

                // EXIF 회전 보정
                img.AutoOrient();

                // 색 관리 단순화(선택)
                img.ColorSpace = ColorSpace.sRGB;

                // 1) 비율 유지로 "박스에 맞춤" 리사이즈 (크롭 금지)
                //    - 두 변 동시에 지정하면 Magick.NET은 기본적으로 '가장 작은 비율'로 줄여 박스 안에 맞춰줌.
                img.Resize(new MagickGeometry(TargetW, TargetH)
                {
                    IgnoreAspectRatio = false // 비율 유지
                    // FillArea = false (기본값) → 박스 내에 맞춤
                });

                // 2) 남는 영역(레터박스)을 중앙 기준으로 채워 고정 캔버스 사이즈로 확장
                //    - UI 배경과 자연스럽게 어울리는 딥 그레이(#1A1D23)
                img.Extent(TargetW, TargetH, Gravity.Center, new MagickColor("#2d2d2d"));

                // 3) 출력 포맷/품질
                img.Format = MagickFormat.Jpg;
                img.Quality = 85;

                // 저장
                using (var outFs = File.Create(cachedPath))
                    await img.WriteAsync(outFs, ct);

                return LoadBitmapFromFile(cachedPath);
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSHA1(string s)
        {
            using var sha1 = SHA1.Create();
            var b = System.Text.Encoding.UTF8.GetBytes(s);
            return string.Concat(sha1.ComputeHash(b).Select(x => x.ToString("x2")));
        }

        private static BitmapImage? LoadBitmapFromFile(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var fs = File.OpenRead(path))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // 파일 잠금 방지
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// 아카이브에서 첫 번째 이미지 항목을 찾아 메모리스트림으로 반환
        /// </summary>
        private static async Task<MemoryStream?> ExtractFirstImageAsync(string archivePath, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { LeaveStreamOpen = false });

                // 첫 이미지(사전순) 선택. 필요시 썸네일 선호 규칙 바꿔도 됨.
                var entry = archive.Entries
                    .Where(e => !e.IsDirectory)
                    .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(e => ImgExt.Contains(Path.GetExtension(e.Key)));

                if (entry == null) return null;

                var cap = (int)Math.Min(entry.Size, 8_000_000); // 힌트: 최대 8MB
                var ms = new MemoryStream(capacity: Math.Max(1024, cap));
                using (var s = entry.OpenEntryStream())
                    s.CopyTo(ms);

                ms.Position = 0;
                return ms;
            }, ct);
        }
    }
}
