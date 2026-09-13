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
    // ==========================================
    // CREAR TARJETA
    // ==========================================

    public static Border Create(
        string machineId,
        AgentInfo? agent,
        Action onClick)
    {
        bool online =
            agent != null &&
            agent.Status.Equals(
                "online",
                StringComparison.OrdinalIgnoreCase
            );

        // ==========================================
        // COLORES
        // ==========================================

        var statusColor =
            online
                ? new SolidColorBrush(
                    Color.FromRgb(
                        50,
                        213,
                        131
                    )
                )
                : new SolidColorBrush(
                    Color.FromRgb(
                        249,
                        112,
                        102
                    )
                );

        var secondaryColor =
            new SolidColorBrush(
                Color.FromRgb(
                    139,
                    147,
                    161
                )
            );

        // ==========================================
        // TARJETA
        // ==========================================

        var card =
            new Border
            {
                Width = 225,

                Height = 155,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            23,
                            26,
                            33
                        )
                    ),

                CornerRadius =
                    new CornerRadius(12),

                Margin =
                    new Thickness(
                        0,
                        0,
                        15,
                        15
                    ),

                Padding =
                    new Thickness(16),

                Cursor =
                    Cursors.Hand
            };

        var content =
            new StackPanel();

        // ==========================================
        // ENCABEZADO
        // ==========================================

        var header =
            new Grid();

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            }
        );

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star
                    )
            }
        );

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            }
        );

        // ==========================================
        // INDICADOR DE ESTADO
        // ==========================================

        var indicator =
            new Ellipse
            {
                Width = 10,

                Height = 10,

                Fill =
                    statusColor,

                Margin =
                    new Thickness(
                        0,
                        5,
                        8,
                        0
                    )
            };

        Grid.SetColumn(
            indicator,
            0
        );

        header.Children.Add(
            indicator
        );

        // ==========================================
        // MACHINE ID
        // ==========================================

        var machineName =
            new TextBlock
            {
                Text =
                    machineId,

                FontSize = 17,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White
            };

        Grid.SetColumn(
            machineName,
            1
        );

        header.Children.Add(
            machineName
        );

        // ==========================================
        // ESTADO
        // ==========================================

        var status =
            new TextBlock
            {
                Text =
                    online
                        ? "ONLINE"
                        : "OFFLINE",

                Foreground =
                    statusColor,

                FontSize = 10,

                FontWeight =
                    FontWeights.Bold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            status,
            2
        );

        header.Children.Add(
            status
        );

        content.Children.Add(
            header
        );

        // ==========================================
        // HOSTNAME
        // ==========================================

        var hostname =
            new TextBlock
            {
                Text =
                    online
                        ? agent!.Hostname
                        : "Sin conexión",

                Foreground =
                    online
                        ? Brushes.White
                        : secondaryColor,

                FontSize = 12,

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        5
                    )
            };

        content.Children.Add(
            hostname
        );

        // ==========================================
        // HEARTBEAT
        // ==========================================

        var heartbeat =
            new TextBlock
            {
                Text =
                    online
                        ? $"Heartbeat: {agent!.LastHeartbeat}"
                        : "Heartbeat: —",

                Foreground =
                    secondaryColor,

                FontSize = 10,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        content.Children.Add(
            heartbeat
        );

        // ==========================================
        // VERSIÓN DEL AGENTE
        // ==========================================

        var version =
            new TextBlock
            {
                Text =
                    online
                        ? $"Agente v{agent!.AgentVersion}"
                        : "Agente no disponible",

                Foreground =
                    secondaryColor,

                FontSize = 10,

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0
                    )
            };

        content.Children.Add(
            version
        );

        // ==========================================
        // CONTENIDO
        // ==========================================

        card.Child =
            content;

        // ==========================================
        // CLICK
        // ==========================================

        card.MouseLeftButtonUp +=
            (_, _) =>
            {
                onClick();
            };

        return card;
    }
}