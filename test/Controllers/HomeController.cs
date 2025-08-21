using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using test.Models;
using ImageMagick;
using ImageMagick.Drawing;

namespace test.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _environment;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ImageControl(IFormFile imageFile)
        {
            try
            {
                if (imageFile == null || imageFile.Length == 0)
                {
                    return Json(new { success = false, error = "Geçerli bir görüntü dosyasý seçiniz." });
                }

                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                var filePath = Path.Combine(uploadsPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                var results = AnalyzeTestKit(filePath);

                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                return Json(new { success = true, results = results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test kit analizi sýrasýnda hata oluþtu");
                return Json(new { success = false, error = "Analiz sýrasýnda bir hata oluþtu: " + ex.Message });
            }
        }

        private List<string> AnalyzeTestKit(string imagePath)
        {
            var results = new List<string>();

            using (var image = new MagickImage(imagePath))
            {
                var debugPath = Path.Combine(_environment.WebRootPath, "debug");
                if (!Directory.Exists(debugPath))
                    Directory.CreateDirectory(debugPath);

                // Görüntüyü 1920x1080'e yeniden boyutlandýr
                image.Resize(1920, 1080);

                var testBoxes = GetFixedTestBoxCoordinates();

                // Debug: Kutucuklarý iþaretle
                using (var debugImage = image.Clone())
                {
                    var drawables = new Drawables();
                    drawables.StrokeColor(MagickColors.Yellow);
                    drawables.StrokeWidth(3);
                    drawables.FillColor(MagickColors.Transparent);
                    drawables.Font("Arial");
                    drawables.FontPointSize(40);

                    for (int i = 0; i < testBoxes.Count; i++)
                    {
                        var box = testBoxes[i];
                        drawables.Rectangle((double)box.X, (double)box.Y, (double)(box.X + box.Width), (double)(box.Y + box.Height));
                        drawables.FillColor(MagickColors.Yellow);
                        drawables.Text((double)(box.X + 20), (double)(box.Y + 50), (i + 1).ToString());
                        drawables.FillColor(MagickColors.Transparent);
                    }

                    drawables.Draw(debugImage);
                    debugImage.Write(Path.Combine(debugPath, "debug_boxes.jpg"));
                }

                // Her kutucuðu analiz et
                for (int i = 0; i < testBoxes.Count && i < 6; i++)
                {
                    var box = testBoxes[i];
                    var result = AnalyzeTestBox(image, box, i + 1, debugPath);
                    results.Add(result);
                }
            }

            return results;
        }

        private List<MagickGeometry> GetFixedTestBoxCoordinates()
        {
            return new List<MagickGeometry>
            {
                new MagickGeometry(100, 200, 200, 600),
                new MagickGeometry(400, 200, 200, 600),
                new MagickGeometry(700, 200, 200, 600),
                new MagickGeometry(1000, 200, 200, 600),
                new MagickGeometry(1300, 200, 200, 600),
                new MagickGeometry(1600, 200, 200, 600)
            };
        }

        private string AnalyzeTestBox(MagickImage image, MagickGeometry box, int boxNumber, string debugPath)
        {
            try
            {
                // Kutucuk alanýný kýrp
                using (var roi = (MagickImage)image.Clone())
                {
                    roi.Crop(box);
                    roi.Write(Path.Combine(debugPath, $"roi_{boxNumber}.jpg"));

                    // Üst ve alt yarýya böl
                    int halfHeight = (int)roi.Height / 2;

                    using (var topRoi = (MagickImage)roi.Clone())
                    using (var bottomRoi = (MagickImage)roi.Clone())
                    {
                        topRoi.Crop(new MagickGeometry(0, 0, (uint)roi.Width, (uint)halfHeight));
                        bottomRoi.Crop(new MagickGeometry(0, (int)halfHeight, (uint)roi.Width, (uint)halfHeight));

                        topRoi.Write(Path.Combine(debugPath, $"top_roi_{boxNumber}.jpg"));
                        bottomRoi.Write(Path.Combine(debugPath, $"bottom_roi_{boxNumber}.jpg"));

                        bool hasTopLine = HasLine(topRoi, boxNumber, "top", debugPath);
                        bool hasBottomLine = HasLine(bottomRoi, boxNumber, "bottom", debugPath);

                        _logger.LogInformation($"Box {boxNumber}: Top={hasTopLine}, Bottom={hasBottomLine}");

                        if (hasTopLine && !hasBottomLine)
                            return $"{boxNumber}. Pozitif";
                        else if (hasTopLine && hasBottomLine)
                            return $"{boxNumber}. Negatif";
                        else
                            return $"{boxNumber}. Geçersiz";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Kutucuk {boxNumber} analizi sýrasýnda hata");
                return $"{boxNumber}. Geçersiz";
            }
        }

        private bool HasLine(MagickImage roi, int boxNumber, string position, string debugPath)
        {
            try
            {
                // Clone iþlemi için explicit casting kullan
                using (var processedImage = (MagickImage)roi.Clone())
                {
                    // Kontrast artýr
                    processedImage.Normalize();
                    processedImage.Contrast();

                    // Edge detection
                    processedImage.Edge(1);

                    // Threshold uygula
                    processedImage.Threshold(new Percentage(50));

                    processedImage.Write(Path.Combine(debugPath, $"processed_{boxNumber}_{position}.jpg"));

                    // Yatay çizgileri tespit et - basit morfoloji
                    using (var horizontalMask = (MagickImage)processedImage.Clone())
                    {
                        // Yatay yapýlarý güçlendir - morphology iþlemleri için kernel tanýmla
                        var kernelSize = Math.Max(3, (int)roi.Width / 10);

                        // Basit eþikleme ile yatay çizgileri tespit et
                        var pixels = horizontalMask.GetPixels();
                        int whitePixelCount = 0;
                        int totalPixels = (int)(horizontalMask.Width * horizontalMask.Height);

                        for (int y = 0; y < (int)horizontalMask.Height; y++)
                        {
                            for (int x = 0; x < (int)horizontalMask.Width; x++)
                            {
                                var pixel = pixels[x, y];
                                var intensity = pixel.GetChannel(0); // R channel for grayscale
                                if (intensity > Quantum.Max * 0.5) // Beyaz pixel
                                {
                                    whitePixelCount++;
                                }
                            }
                        }

                        horizontalMask.Write(Path.Combine(debugPath, $"horizontal_{boxNumber}_{position}.jpg"));

                        // Yatay çizgi varlýðýný kontrol et
                        double whiteRatio = (double)whitePixelCount / totalPixels;

                        _logger.LogInformation($"Box {boxNumber} {position}: White ratio = {whiteRatio:F3}");

                        return whiteRatio > 0.1; // %10'dan fazla beyaz pixel
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Çizgi tespiti hatasý - Box {boxNumber} {position}");
                return false;
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}