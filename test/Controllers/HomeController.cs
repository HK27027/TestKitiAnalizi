using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using test.Models;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

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
                    return Json(new { success = false, error = "Ge�erli bir g�r�nt� dosyas� se�iniz." });
                }

                // Dosyay� ge�ici olarak kaydet
                var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                    Directory.CreateDirectory(uploadsPath);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                var filePath = Path.Combine(uploadsPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                // Test kit analizi yap
                var results = AnalyzeTestKit(filePath);

                // Ge�ici dosyay� sil
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);

                return Json(new { success = true, results = results });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test kit analizi s�ras�nda hata olu�tu");
                return Json(new { success = false, error = "Analiz s�ras�nda bir hata olu�tu: " + ex.Message });
            }
        }

        private List<string> AnalyzeTestKit(string imagePath)
        {
            var results = new List<string>();

            using (Mat image = CvInvoke.Imread(imagePath, ImreadModes.Color))
            {
                if (image.IsEmpty)
                {
                    throw new Exception("G�r�nt� y�klenemedi");
                }

                // Debug i�in resmi kaydet
                var debugPath = Path.Combine(_environment.WebRootPath, "debug");
                if (!Directory.Exists(debugPath))
                    Directory.CreateDirectory(debugPath);

                // G�r�nt�y� 1920x1080'e yeniden boyutland�r (e�er farkl�ysa)
                Mat resizedImage = new Mat();
                CvInvoke.Resize(image, resizedImage, new Size(1920, 1080));

                // Sabit koordinatlar� kullan
                var testBoxes = GetFixedTestBoxCoordinates();

                // Debug: Kutucuklar� g�r�nt� �zerinde i�aretle
                Mat debugImage = resizedImage.Clone();
                for (int i = 0; i < testBoxes.Count; i++)
                {
                    var box = testBoxes[i];
                    CvInvoke.Rectangle(debugImage, box, new MCvScalar(0, 255, 255), 3); // Sar� �er�eve
                    CvInvoke.PutText(debugImage, (i + 1).ToString(),
                        new Point(box.X + 20, box.Y + 50),
                        FontFace.HersheySimplex, 2, new MCvScalar(0, 255, 255), 3);
                }
                CvInvoke.Imwrite(Path.Combine(debugPath, "debug_boxes.jpg"), debugImage);

                // Her kutucu�u analiz et
                for (int i = 0; i < testBoxes.Count && i < 6; i++)
                {
                    var box = testBoxes[i];
                    var result = AnalyzeTestBoxWithColor(resizedImage, box, i + 1, debugPath);
                    results.Add(result);
                }
            }

            return results;
        }

        private List<Rectangle> GetFixedTestBoxCoordinates()
        {
            var boxes = new List<Rectangle>
            {
                new Rectangle(100, 200, 200, 600),   // 1. Kutucuk
                new Rectangle(400, 200, 200, 600),   // 2. Kutucuk
                new Rectangle(700, 200, 200, 600),   // 3. Kutucuk
                new Rectangle(1000, 200, 200, 600),  // 4. Kutucuk
                new Rectangle(1300, 200, 200, 600),  // 5. Kutucuk
                new Rectangle(1600, 200, 200, 600)   // 6. Kutucuk
            };

            return boxes;
        }

        private string AnalyzeTestBoxWithColor(Mat image, Rectangle box, int boxNumber, string debugPath)
        {
            try
            {
                // Kutucuk alan�n� k�rp
                Mat roi = new Mat(image, box);

                // Debug i�in ROI'yi kaydet
                CvInvoke.Imwrite(Path.Combine(debugPath, $"roi_{boxNumber}.jpg"), roi);

                // Kutucu�u �st ve alt yar�ya b�l
                int halfHeight = roi.Height / 2;
                Rectangle topHalf = new Rectangle(0, 0, roi.Width, halfHeight);
                Rectangle bottomHalf = new Rectangle(0, halfHeight, roi.Width, roi.Height - halfHeight);

                Mat topRoi = new Mat(roi, topHalf);
                Mat bottomRoi = new Mat(roi, bottomHalf);

                // Debug i�in �st ve alt ROI'leri kaydet
                CvInvoke.Imwrite(Path.Combine(debugPath, $"top_roi_{boxNumber}.jpg"), topRoi);
                CvInvoke.Imwrite(Path.Combine(debugPath, $"bottom_roi_{boxNumber}.jpg"), bottomRoi);

                // Her yar�da k�rm�z�/mor �izgi var m� kontrol et
                bool hasTopLine = HasColoredLine(topRoi, boxNumber, "top", debugPath);
                bool hasBottomLine = HasColoredLine(bottomRoi, boxNumber, "bottom", debugPath);

                _logger.LogInformation($"Box {boxNumber}: Top={hasTopLine}, Bottom={hasBottomLine}");

                // Kurallara g�re sonu� belirle
                if (hasTopLine && !hasBottomLine)
                {
                    return $"{boxNumber}. Pozitif";
                }
                else if (hasTopLine && hasBottomLine)
                {
                    return $"{boxNumber}. Negatif";
                }
                else
                {
                    return $"{boxNumber}. Ge�ersiz";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Kutucuk {boxNumber} analizi s�ras�nda hata");
                return $"{boxNumber}. Ge�ersiz";
            }
        }

        private bool HasColoredLine(Mat roi, int boxNumber, string position, string debugPath)
        {
            try
            {
                // HSV color space'e �evir (daha iyi renk tespiti i�in)
                Mat hsvImage = new Mat();
                CvInvoke.CvtColor(roi, hsvImage, ColorConversion.Bgr2Hsv);

                // K�rm�z� renk aral�klar� (HSV'de k�rm�z� iki aral�kta bulunur)
                Mat redMask1 = new Mat();
                Mat redMask2 = new Mat();
                Mat purpleMask = new Mat();
                Mat blackMask = new Mat();

                // K�rm�z� aral�k 1: 0-10
                CvInvoke.InRange(hsvImage, new ScalarArray(new MCvScalar(0, 50, 50)),
                                         new ScalarArray(new MCvScalar(10, 255, 255)), redMask1);

                // K�rm�z� aral�k 2: 170-180
                CvInvoke.InRange(hsvImage, new ScalarArray(new MCvScalar(170, 50, 50)),
                                         new ScalarArray(new MCvScalar(180, 255, 255)), redMask2);

                // Mor aral���: 120-160
                CvInvoke.InRange(hsvImage, new ScalarArray(new MCvScalar(120, 50, 50)),
                                         new ScalarArray(new MCvScalar(160, 255, 255)), purpleMask);

                // Siyah aral���: D���k V (Value) de�eri olan t�m renkler
                // H ve S de�eri �nemli de�il, sadece V de�eri d���k olan (karanl�k) renkler
                CvInvoke.InRange(hsvImage, new ScalarArray(new MCvScalar(0, 0, 0)),
                                         new ScalarArray(new MCvScalar(180, 255, 80)), blackMask);

                // T�m maskeleri birle�tir
                Mat combinedMask = new Mat();
                CvInvoke.BitwiseOr(redMask1, redMask2, combinedMask);
                CvInvoke.BitwiseOr(combinedMask, purpleMask, combinedMask);
                CvInvoke.BitwiseOr(combinedMask, blackMask, combinedMask);

                // Debug i�in maskeyi kaydet
                CvInvoke.Imwrite(Path.Combine(debugPath, $"mask_{boxNumber}_{position}.jpg"), combinedMask);

                // Morfolojik i�lemler (g�r�lt�y� temizle, �izgileri g��lendir)
                Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(3, 3), new Point(-1, -1));
                CvInvoke.MorphologyEx(combinedMask, combinedMask, MorphOp.Close, kernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

                // Yatay �izgileri g��lendirmek i�in �zel kernel
                Mat horizontalKernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(roi.Width / 4, 3), new Point(-1, -1));
                Mat horizontalMask = new Mat();
                CvInvoke.MorphologyEx(combinedMask, horizontalMask, MorphOp.Open, horizontalKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

                // Debug i�in horizontal mask'� kaydet
                CvInvoke.Imwrite(Path.Combine(debugPath, $"horizontal_mask_{boxNumber}_{position}.jpg"), horizontalMask);

                // Contours bul
                VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
                Mat hierarchy = new Mat();
                CvInvoke.FindContours(horizontalMask, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

                // �izgi kriterlerini kontrol et
                for (int i = 0; i < contours.Size; i++)
                {
                    double area = CvInvoke.ContourArea(contours[i]);
                    var boundingRect = CvInvoke.BoundingRectangle(contours[i]);

                    // Minimum alan kontrol� (ROI'nin %2'si)
                    double minArea = roi.Width * roi.Height * 0.02;

                    // Yatay �izgi kontrol� (geni�lik > y�kseklik * 3)
                    bool isHorizontal = boundingRect.Width > boundingRect.Height * 3;

                    // Minimum geni�lik kontrol� (ROI geni�li�inin %30'u)
                    bool hasMinWidth = boundingRect.Width > roi.Width * 0.3;

                    // Minimum y�kseklik kontrol� (�ok ince �izgiler i�in)
                    bool hasMinHeight = boundingRect.Height >= 2;

                    // Pozisyon kontrol� (�izgi ROI'nin ortalar�nda m�?)
                    bool isInValidPosition = boundingRect.Y > roi.Height * 0.2 &&
                                           boundingRect.Y + boundingRect.Height < roi.Height * 0.8;

                    _logger.LogInformation($"Box {boxNumber} {position}: Area={area:F0}, MinArea={minArea:F0}, " +
                                         $"Rect=({boundingRect.X},{boundingRect.Y},{boundingRect.Width},{boundingRect.Height}), " +
                                         $"Horizontal={isHorizontal}, MinWidth={hasMinWidth}, MinHeight={hasMinHeight}, ValidPos={isInValidPosition}");

                    if (area > minArea && isHorizontal && hasMinWidth && hasMinHeight && isInValidPosition)
                    {
                        // Debug: Bulunan �izgiyi i�aretle
                        Mat debugContour = roi.Clone();
                        CvInvoke.Rectangle(debugContour, boundingRect, new MCvScalar(0, 255, 0), 2);
                        CvInvoke.Imwrite(Path.Combine(debugPath, $"found_line_{boxNumber}_{position}.jpg"), debugContour);

                        return true;
                    }
                }

                // E�er contour bulunamad�ysa, piksel yo�unlu�u kontrol et
                double whitePixelRatio = CvInvoke.CountNonZero(horizontalMask) / (double)(horizontalMask.Width * horizontalMask.Height);
                _logger.LogInformation($"Box {boxNumber} {position}: White pixel ratio = {whitePixelRatio:F3}");

                // E�er yeterince beyaz piksel varsa �izgi var kabul et
                return whitePixelRatio > 0.05; // %5'ten fazla beyaz piksel
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Renk tespiti s�ras�nda hata - Box {boxNumber} {position}");
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