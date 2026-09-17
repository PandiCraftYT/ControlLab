using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ControlLab.Agent;

public static class ScreenCapture
{
    // ==========================================
    // CAPTURA NORMAL
    // ==========================================

    public static string CaptureScreen()
    {
        string filePath = Path.Combine(
            AppContext.BaseDirectory,
            "captura-prueba.jpg"
        );

        var bounds =
            System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, 1280, 720);

        using var bitmap =
            new Bitmap(
                bounds.Width,
                bounds.Height
            );

        using (var graphics =
            Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size
            );
        }

        using var resized =
            ResizeImage(
                bitmap,
                1280,
                720
            );

        SaveJpeg(
            resized,
            filePath,
            60L
        );

        return filePath;
    }

    // ==========================================
    // CAPTURA EXCLUSIVA PARA PREVIEW
    // ==========================================

    public static string CapturePreview()
    {
        string filePath = Path.Combine(
            AppContext.BaseDirectory,
            "preview.jpg"
        );

        var bounds =
            System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, 1280, 720);

        using var bitmap =
            new Bitmap(
                bounds.Width,
                bounds.Height
            );

        using (var graphics =
            Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bounds.Size
            );
        }

        using var preview =
            ResizeImage(
                bitmap,
                320,
                180
            );

        SaveJpeg(
            preview,
            filePath,
            50L
        );

        return filePath;
    }

    // ==========================================
    // REDIMENSIONAR IMAGEN
    // ==========================================

    private static Bitmap ResizeImage(
        Bitmap source,
        int maxWidth,
        int maxHeight)
    {
        double ratioX =
            (double)maxWidth / source.Width;

        double ratioY =
            (double)maxHeight / source.Height;

        double ratio =
            Math.Min(
                ratioX,
                ratioY
            );

        int width =
            Math.Max(
                1,
                (int)(source.Width * ratio)
            );

        int height =
            Math.Max(
                1,
                (int)(source.Height * ratio)
            );

        var result =
            new Bitmap(
                width,
                height
            );

        using var graphics =
            Graphics.FromImage(result);

        graphics.InterpolationMode =
            InterpolationMode.HighQualityBicubic;

        graphics.CompositingQuality =
            CompositingQuality.HighQuality;

        graphics.SmoothingMode =
            SmoothingMode.HighQuality;

        graphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        graphics.DrawImage(
            source,
            new Rectangle(
                0,
                0,
                width,
                height
            )
        );

        return result;
    }

    // ==========================================
    // GUARDAR JPEG
    // ==========================================

    private static void SaveJpeg(
        Bitmap bitmap,
        string filePath,
        long quality)
    {
        ImageCodecInfo? jpegCodec =
            ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(
                    codec =>
                        codec.FormatID ==
                        ImageFormat.Jpeg.Guid
                );

        if (jpegCodec == null)
        {
            bitmap.Save(
                filePath,
                ImageFormat.Jpeg
            );

            return;
        }

        using var encoderParameters =
            new EncoderParameters(1);

        encoderParameters.Param[0] =
            new EncoderParameter(
                Encoder.Quality,
                quality
            );

        bitmap.Save(
            filePath,
            jpegCodec,
            encoderParameters
        );
    }

   public static byte[] CaptureStreamFrame()
    {
        Screen screen =
            Screen.PrimaryScreen
            ?? throw new InvalidOperationException(
                "No se encontró la pantalla principal."
            );

        Rectangle bounds =
            screen.Bounds;

        using var source =
            new Bitmap(
                bounds.Width,
                bounds.Height
            );

        using (Graphics graphics =
            Graphics.FromImage(source))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                source.Size
            );
        }

        const int maxWidth = 640;
        const int maxHeight = 360;

        double scale =
            Math.Min(
                (double)maxWidth / source.Width,
                (double)maxHeight / source.Height
            );

        int width =
            Math.Max(
                1,
                (int)Math.Round(
                    source.Width * scale
                )
            );

        int height =
            Math.Max(
                1,
                (int)Math.Round(
                    source.Height * scale
                )
            );

        using var resized =
            new Bitmap(
                width,
                height
            );

        using (Graphics graphics =
            Graphics.FromImage(resized))
        {
            graphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            graphics.SmoothingMode =
                System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            graphics.PixelOffsetMode =
                System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            graphics.DrawImage(
                source,
                new Rectangle(
                    0,
                    0,
                    width,
                    height
                )
            );
        }

        using var stream =
            new MemoryStream();

        var jpegCodec =
            System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec =>
                    codec.FormatID ==
                    System.Drawing.Imaging.ImageFormat.Jpeg.Guid
                );

        if (jpegCodec == null)
            throw new InvalidOperationException(
                "No se encontró el codificador JPEG."
            );

        using var encoderParameters =
            new System.Drawing.Imaging.EncoderParameters(1);

        encoderParameters.Param[0] =
            new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality,
                50L
            );

        resized.Save(
            stream,
            jpegCodec,
            encoderParameters
        );

        return stream.ToArray();
    }
}