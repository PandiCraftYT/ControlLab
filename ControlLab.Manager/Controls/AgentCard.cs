using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ControlLab.Manager.Models;

namespace ControlLab.Manager.Controls;

public static class AgentCard
{
    public static Border Create(
        string machineId,
        AgentInfo? agent,
        Action onClick)
    {
        bool online =
            agent != null &&
            agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase);

        bool authorized =
            agent != null &&
            agent.Authorized;

        string displayName =
            !string.IsNullOrWhiteSpace(agent?.DisplayName)
                ? agent.DisplayName
                : machineId;


        // ==========================================
        // COLORES DE ESTADO
        // ==========================================

        var statusColor =
            online
                ? Color.FromRgb(50, 213, 131)
                : Color.FromRgb(249, 112, 102);

        var authorizationColor =
            authorized
                ? Color.FromRgb(50, 213, 131)
                : Color.FromRgb(255, 180, 70);


        var cardBackground =
            new SolidColorBrush(
                Color.FromRgb(17, 24, 33));

        var borderBrush =
            new SolidColorBrush(
                Color.FromRgb(32, 43, 55));


        // ==========================================
        // TARJETA
        // ==========================================

        var card = new Border
        {
            Width = 250,
            Height = 190,

            Background = cardBackground,

            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),

            CornerRadius = new CornerRadius(12),

            Margin = new Thickness(0, 0, 14, 14),

            Padding = new Thickness(16),

            Cursor = Cursors.Hand
        };


        // ==========================================
        // CONTENIDO
        // ==========================================

        var content =
            new Grid();


        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = GridLength.Auto
            });


        // ==========================================
        // ENCABEZADO
        // ==========================================

        var header =
            new Grid();

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });


        // ==========================================
        // ICONO PC
        // ==========================================

        var computerIcon =
            new Border
            {
                Width = 42,
                Height = 42,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            19,
                            33,
                            47)),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            35,
                            59,
                            80)),

                BorderThickness =
                    new Thickness(1),

                CornerRadius =
                    new CornerRadius(9)
            };


        var computer =
            new Grid
            {
                Width = 30,
                Height = 30
            };


        // Monitor

        var monitor =
            new Border
            {
                Width = 25,
                Height = 18,

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            85,
                            169,
                            255)),

                BorderThickness =
                    new Thickness(2),

                CornerRadius =
                    new CornerRadius(2),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Top,

                Margin =
                    new Thickness(0, 2, 0, 0)
            };

        computer.Children.Add(monitor);


        // Soporte

        var support =
            new Rectangle
            {
                Width = 2,
                Height = 6,

                Fill =
                    new SolidColorBrush(
                        Color.FromRgb(
                            85,
                            169,
                            255)),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Top,

                Margin =
                    new Thickness(0, 20, 0, 0)
            };

        computer.Children.Add(support);


        // Base

        var baseLine =
            new Rectangle
            {
                Width = 15,
                Height = 2,

                Fill =
                    new SolidColorBrush(
                        Color.FromRgb(
                            85,
                            169,
                            255)),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Bottom,

                Margin =
                    new Thickness(0, 0, 0, 2)
            };

        computer.Children.Add(baseLine);


        computerIcon.Child = computer;

        Grid.SetColumn(
            computerIcon,
            0);

        header.Children.Add(
            computerIcon);


        // ==========================================
        // NOMBRE
        // ==========================================

        var namePanel =
            new StackPanel
            {
                Margin =
                    new Thickness(11, 1, 6, 0),

                VerticalAlignment =
                    VerticalAlignment.Center
            };


        var machineName =
            new TextBlock
            {
                Text = displayName,

                FontSize = 15,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        namePanel.Children.Add(
            machineName);


        var machineIdText =
            new TextBlock
            {
                Text = machineId,

                FontSize = 9,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            101,
                            120,
                            141)),

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(0, 2, 0, 0)
            };

        namePanel.Children.Add(
            machineIdText);


        Grid.SetColumn(
            namePanel,
            1);

        header.Children.Add(
            namePanel);


        // ==========================================
        // ESTADO ONLINE / OFFLINE
        // ==========================================

        var status =
            new TextBlock
            {
                Text =
                    online
                        ? "ONLINE"
                        : "OFFLINE",

                Foreground =
                    new SolidColorBrush(
                        statusColor),

                FontSize = 9,

                FontWeight =
                    FontWeights.Bold,

                VerticalAlignment =
                    VerticalAlignment.Top
            };

        Grid.SetColumn(
            status,
            2);

        header.Children.Add(
            status);


        Grid.SetRow(
            header,
            0);

        content.Children.Add(
            header);


        // ==========================================
        // AUTORIZACIÓN
        // ==========================================

        var authorization =
            new TextBlock
            {
                Text =
                    authorized
                        ? "✓ AUTORIZADO"
                        : "⚠ PENDIENTE",

                Foreground =
                    new SolidColorBrush(
                        authorizationColor),

                FontSize = 10,

                FontWeight =
                    FontWeights.Bold,

                Margin =
                    new Thickness(
                        53,
                        5,
                        0,
                        0)
            };

        Grid.SetRow(
            authorization,
            1);

        content.Children.Add(
            authorization);


        // ==========================================
        // INFORMACIÓN
        // ==========================================

        var info =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        0)
            };


        // HOSTNAME

        var hostnameLabel =
            new TextBlock
            {
                Text = "HOSTNAME",

                FontSize = 8,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            101,
                            120,
                            141))
            };

        info.Children.Add(
            hostnameLabel);


        var hostname =
            new TextBlock
            {
                Text =
                    online
                        ? agent!.Hostname
                        : "Sin conexión",

                FontSize = 11,

                Foreground =
                    online
                        ? Brushes.White
                        : new SolidColorBrush(
                            Color.FromRgb(
                                102,
                                119,
                                138)),

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        7)
            };

        info.Children.Add(
            hostname);


        // HEARTBEAT

        var heartbeatLabel =
            new TextBlock
            {
                Text = "ÚLTIMO HEARTBEAT",

                FontSize = 8,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            101,
                            120,
                            141))
            };

        info.Children.Add(
            heartbeatLabel);


        var heartbeat =
            new TextBlock
            {
                Text =
                    online
                        ? agent!.LastHeartbeat
                        : "—",

                FontSize = 10,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            147,
                            164,
                            184)),

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        0)
            };

        info.Children.Add(
            heartbeat);


        Grid.SetRow(
            info,
            2);

        content.Children.Add(
            info);


        // ==========================================
        // PIE DE TARJETA
        // ==========================================

        var footer =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        7,
                        0,
                        0)
            };


        footer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        footer.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });


        var version =
            new TextBlock
            {
                Text =
                    online
                        ? $"Agente v{agent!.AgentVersion}"
                        : "Agente no disponible",

                FontSize = 9,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            102,
                            119,
                            138))
            };

        Grid.SetColumn(
            version,
            0);

        footer.Children.Add(
            version);


        var arrow =
            new TextBlock
            {
                Text = "→",

                FontSize = 16,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            85,
                            169,
                            255)),

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            arrow,
            1);

        footer.Children.Add(
            arrow);


        Grid.SetRow(
            footer,
            3);

        content.Children.Add(
            footer);


        card.Child = content;


        // ==========================================
        // EFECTO AL PASAR EL MOUSE
        // ==========================================

        card.MouseEnter += (_, _) =>
        {
            card.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        23,
                        34,
                        46));

            card.BorderBrush =
                new SolidColorBrush(
                    Color.FromRgb(
                        53,
                        82,
                        108));
        };


        card.MouseLeave += (_, _) =>
        {
            card.Background =
                cardBackground;

            card.BorderBrush =
                borderBrush;
        };


        // ==========================================
        // ABRIR DETALLES
        // ==========================================

        card.MouseLeftButtonUp += (_, _) =>
        {
            onClick();
        };


        return card;
    }
}