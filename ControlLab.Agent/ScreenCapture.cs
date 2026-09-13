using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ControlLab.Agent;

public static class ScreenCapture
{
    public static string CaptureScreen()
    {
        string filePath = Path.Combine(
            AppContext.BaseDirectory,
            "captura-prueba.jpg"
        );

        Rectangle bounds =
            System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? throw new InvalidOperationException(
                "No se pudo obtener el tamaño de la pantalla."
            );

        using Bitmap original = new(
            bounds.Width,
            bounds.Height
        );

        using Graphics graphics =
            Graphics.FromImage(original);

        graphics.CopyFromScreen(
            bounds.Left,
            bounds.Top,
            0,
            0,
            bounds.Size
        );

        // ==========================================
        // REDUCIR A 1280x720 COMO MÁXIMO
        // ==========================================

        const int maxWidth = 1280;
        const int maxHeight = 720;

        double scale =
            Math.Min(
                (double)maxWidth / original.Width,
                (double)maxHeight / original.Height
            );

        scale = Math.Min(scale, 1.0);

        int newWidth =
            Math.Max(
                1,
                (int)(original.Width * scale)
            );

        int newHeight =
            Math.Max(
                1,
                (int)(original.Height * scale)
            );

        using Bitmap resized =
            new(
                newWidth,
                newHeight
            );

        using Graphics resizedGraphics =
            Graphics.FromImage(resized);

        resizedGraphics.InterpolationMode =
            InterpolationMode.HighQualityBicubic;

        resizedGraphics.CompositingQuality =
            CompositingQuality.HighQuality;

        resizedGraphics.SmoothingMode =
            SmoothingMode.HighQuality;

        resizedGraphics.PixelOffsetMode =
            PixelOffsetMode.HighQuality;

        resizedGraphics.DrawImage(
            original,
            new Rectangle(
                0,
                0,
                newWidth,
                newHeight
            )
        );

        // ==========================================
        // JPEG CALIDAD 60
        // ==========================================

        ImageCodecInfo? jpegCodec =
            ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(
                    codec =>
                        codec.FormatID ==
                        ImageFormat.Jpeg.Guid
                );

        if (jpegCodec == null)
        {
            resized.Save(
                filePath,
                ImageFormat.Jpeg
            );

            return filePath;
        }

        using EncoderParameters encoderParameters =
            new(1);

        encoderParameters.Param[0] =
            new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality,
                60L
            );

        resized.Save(
            filePath,
            jpegCodec,
            encoderParameters
        );

        return filePath;
    }
}