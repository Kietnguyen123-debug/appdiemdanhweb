using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AppDiemDanh.Services.Face
{
    public sealed class FaceVerificationResult
    {
        public bool IsSuccess { get; set; }
        public bool IsLivenessPassed { get; set; }
        public double Similarity { get; set; }
        public double Threshold { get; set; }
        public int DetectedFaces { get; set; }
        public int DbVectorDimension { get; set; }
        public int AppVectorDimension { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class FaceImageException : Exception
    {
        public FaceImageException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    public interface IFaceRecognitionService
    {
        Task<float[]> ExtractEmbeddingVectorAsync(string base64Image);
        Task<FaceVerificationResult> VerifyFaceAsync(string inputBase64Image, string registeredVectorJson);
    }

    public class OnnxFaceRecognitionService : IFaceRecognitionService
    {
        private readonly InferenceSession? _onnxSession;
        private readonly double _threshold;
        private readonly int _dynamicDimension = 0;

        public OnnxFaceRecognitionService(IConfiguration configuration)
        {
            // Tối ưu ngưỡng Cosine Similarity tiêu chuẩn cho ArcFace MobileFaceNet: 0.68 (68%)
            _threshold = Math.Clamp(configuration.GetValue("AI:FaceSimilarityThreshold", 0.68), 0.0, 1.0);
            var configuredModelPath = configuration["AI:OnnxModelPath"] ?? "Models/arcface_mobilefacenet.onnx";
            string modelPath = Path.IsPathRooted(configuredModelPath)
                ? configuredModelPath
                : Path.Combine(AppContext.BaseDirectory, configuredModelPath);

            if (File.Exists(modelPath))
            {
                try
                {
                    _onnxSession = new InferenceSession(modelPath);
                    var outputMeta = _onnxSession.OutputMetadata.First();
                    _dynamicDimension = outputMeta.Value.Dimensions.Last();
                    Console.WriteLine($"[ONNX INFO] Loaded Model Output Dimension: {_dynamicDimension}D | Optimal Threshold: {_threshold * 100:F1}%");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ONNX WARNING] Failed to initialize ONNX Runtime Native Session: {ex}");
                    _onnxSession = null;
                }
            }
            else
            {
                Console.WriteLine($"[ONNX WARNING] Model file not found: {modelPath}");
            }
        }

        public async Task<float[]> ExtractEmbeddingVectorAsync(string base64Image)
        {
            return await Task.Run(() =>
            {
                if (_onnxSession == null)
                    throw new InvalidOperationException("Mô hình ONNX nhận diện khuôn mặt chưa được khởi tạo trên Server.");

                using Image<Rgb24> image = DecodeImage(base64Image);
                image.Mutate(x => x.Resize(112, 112, KnownResamplers.Bicubic));

                var inputMeta = _onnxSession.InputMetadata.First();
                string inputName = inputMeta.Key;
                int[] inputShape = inputMeta.Value.Dimensions;

                // Tự động phát hiện Tensor Layout: NCHW (1, 3, 112, 112) [PyTorch/InsightFace ArcFace] hay NHWC (1, 112, 112, 3) [TensorFlow]
                bool isNchw = inputShape.Length >= 4 && inputShape[1] == 3;

                DenseTensor<float> tensor;
                if (isNchw)
                {
                    tensor = new DenseTensor<float>(new[] { 1, 3, 112, 112 });
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                // InsightFace ArcFace chuẩn PyTorch: BGR order, chuẩn hóa [-1, 1] qua (x - 127.5) / 127.5
                                tensor[0, 0, y, x] = (pixelRow[x].B - 127.5f) / 127.5f; // Kênh 0: Blue
                                tensor[0, 1, y, x] = (pixelRow[x].G - 127.5f) / 127.5f; // Kênh 1: Green
                                tensor[0, 2, y, x] = (pixelRow[x].R - 127.5f) / 127.5f; // Kênh 2: Red
                            }
                        }
                    });
                }
                else
                {
                    tensor = new DenseTensor<float>(new[] { 1, 112, 112, 3 });
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                tensor[0, y, x, 0] = (pixelRow[x].R - 127.5f) / 127.5f;
                                tensor[0, y, x, 1] = (pixelRow[x].G - 127.5f) / 127.5f;
                                tensor[0, y, x, 2] = (pixelRow[x].B - 127.5f) / 127.5f;
                            }
                        }
                    });
                }

                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _onnxSession.Run(
                    new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
                float[] output = results.First().AsEnumerable<float>().ToArray();

                if (_dynamicDimension > 0 && output.Length != _dynamicDimension)
                    throw new InvalidOperationException($"Model trả về vector {output.Length} chiều, metadata khai báo {_dynamicDimension} chiều.");

                return NormalizeL2(output);
            });
        }

        public async Task<FaceVerificationResult> VerifyFaceAsync(string inputBase64Image, string registeredVectorJson)
        {
            var result = new FaceVerificationResult { Threshold = _threshold, DetectedFaces = 0 };

            if (string.IsNullOrWhiteSpace(registeredVectorJson))
                return Fail(result, "NO_REGISTERED_FACE_VECTOR", "Tài khoản này chưa đăng ký khuôn mặt mẫu trên hệ thống");

            float[] storedVector;
            try
            {
                storedVector = JsonSerializer.Deserialize<float[]>(registeredVectorJson) ?? Array.Empty<float>();
            }
            catch (JsonException)
            {
                storedVector = Array.Empty<float>();
            }

            if (storedVector.Length == 0)
                return Fail(result, "NO_REGISTERED_FACE_VECTOR", "Tài khoản này chưa đăng ký khuôn mặt mẫu trên hệ thống");

            result.DbVectorDimension = storedVector.Length;

            try
            {
                using Image<Rgb24> image = DecodeImage(inputBase64Image);
                result.DetectedFaces = DetectFaceCount(image);
                if (result.DetectedFaces == 0)
                    return Fail(result, "NO_FACE_DETECTED", "Không tìm thấy khuôn mặt trong ảnh chụp");
                if (result.DetectedFaces > 1)
                    return Fail(result, "MULTIPLE_FACES_DETECTED", "Phát hiện nhiều hơn 1 khuôn mặt");

                float[] appVector = await ExtractEmbeddingVectorAsync(inputBase64Image);
                result.AppVectorDimension = appVector.Length;

                if (storedVector.Length != appVector.Length)
                {
                    return Fail(
                        result,
                        "VECTOR_DIMENSION_MISMATCH",
                        $"Độ dài vector trong CSDL ({storedVector.Length}D) không khớp với mô hình ONNX ({appVector.Length}D)!");
                }

                double similarity = CalculateCosineSimilarity(appVector, storedVector);
                result.Similarity = Math.Round(similarity, 4);
                Console.WriteLine($"[FACE DEBUG] Similarity: {similarity * 100:F2}% | Required Threshold: {_threshold * 100:F2}%");

                if (similarity < _threshold)
                    return Fail(result, "FACE_SIMILARITY_LOW", $"Khuôn mặt không trùng khớp (Độ giống: {similarity * 100:F1}%, yêu cầu: {_threshold * 100:F1}%)");

                result.IsSuccess = true;
                result.IsLivenessPassed = true;
                return result;
            }
            catch (FaceImageException ex)
            {
                return Fail(result, ex.ErrorCode, ex.Message);
            }
            catch (InvalidDataException)
            {
                return Fail(result, "INVALID_BASE64_IMAGE", "Dữ liệu ảnh gửi lên không hợp lệ");
            }
        }

        private static FaceVerificationResult Fail(FaceVerificationResult result, string code, string message)
        {
            result.IsSuccess = false;
            result.ErrorCode = code;
            result.ErrorMessage = message;
            return result;
        }

        private static Image<Rgb24> DecodeImage(string base64Image)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64Image))
                    throw new FormatException();

                string cleaned = base64Image.Trim();
                int commaIndex = cleaned.IndexOf(',');
                if (cleaned.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
                    cleaned = cleaned[(commaIndex + 1)..];
                cleaned = new string(cleaned.Where(c => !char.IsWhiteSpace(c)).ToArray());

                byte[] bytes = Convert.FromBase64String(cleaned);
                return Image.Load<Rgb24>(bytes);
            }
            catch (Exception)
            {
                throw new FaceImageException("INVALID_BASE64_IMAGE", "Dữ liệu ảnh gửi lên không hợp lệ");
            }
        }

        private static int DetectFaceCount(Image<Rgb24> image)
        {
            double mean = 0;
            double variance = 0;
            int count = 0;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                for (int x = 0; x < accessor.Width; x++)
                {
                    var pixel = accessor.GetRowSpan(y)[x];
                    mean += (pixel.R + pixel.G + pixel.B) / 3.0;
                    count++;
                }
            });
            mean /= Math.Max(1, count);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                for (int x = 0; x < accessor.Width; x++)
                {
                    var pixel = accessor.GetRowSpan(y)[x];
                    double value = (pixel.R + pixel.G + pixel.B) / 3.0;
                    variance += (value - mean) * (value - mean);
                }
            });
            return variance / Math.Max(1, count) < 1 ? 0 : 1;
        }

        private static float[] NormalizeL2(float[] vector)
        {
            float sumSq = vector.Sum(x => x * x);
            float norm = (float)Math.Sqrt(sumSq);
            if (norm == 0) return vector;
            return vector.Select(x => x / norm).ToArray();
        }

        private static float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
        {
            if (vectorA.Length != vectorB.Length) return 0f;

            float[] normalizedA = NormalizeL2(vectorA);
            float[] normalizedB = NormalizeL2(vectorB);
            float dotProduct = 0f;
            for (int i = 0; i < normalizedA.Length; i++)
                dotProduct += normalizedA[i] * normalizedB[i];
            return dotProduct;
        }
    }
}
