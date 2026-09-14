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
                StringComparison.OrdinalIgnoreCase
            );

        bool authorized =
            agent != null &&
            agent.Authorized;

        string displayName =
            !string.IsNullOrWhiteSpace(agent?.DisplayName)
                ? agent.DisplayName
                : machineId;

        // ==========================================
        // COLOR DE CONEXIÓN
        // ==========================================

        var statusColor =
            online
                ? new SolidColorBrush(
                    Color.FromRgb(50, 213, 131))
                : new SolidColorBrush(
                    Color.FromRgb(249, 112, 102));

        // ==========================================
        // COLOR DE AUTORIZACIÓN
        // ==========================================

        var authorizationColor =
            authorized
                ? new SolidColorBrush(
                    Color.FromRgb(50, 213, 131))
                : new SolidColorBrush(
                    Color.FromRgb(255, 180, 70));

        var secondaryColor =
            new SolidColorBrush(
                Color.FromRgb(139, 147, 161));

        // ==========================================
        // TARJETA
        // ==========================================

        var card = new Border
        {
            Width = 225,
            Height = 180,

            Background =
                new SolidColorBrush(
                    Color.FromRgb(23, 26, 33)),

            CornerRadius =
                new CornerRadius(12),

            Margin =
                new Thickness(0, 0, 15, 15),

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
                Width = GridLength.Auto
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        // ==========================================
        // INDICADOR ONLINE / OFFLINE
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
                        0)
            };

        Grid.SetColumn(
            indicator,
            0);

        header.Children.Add(
            indicator);

        // ==========================================
        // NOMBRE
        // ==========================================

        var machineName =
            new TextBlock
            {
                Text =
                    displayName,

                FontSize =
                    17,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    Brushes.White,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        Grid.SetColumn(
            machineName,
            1);

        header.Children.Add(
            machineName);

        // ==========================================
        // ESTADO CONEXIÓN
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

                FontSize =
                    10,

                FontWeight =
                    FontWeights.Bold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        Grid.SetColumn(
            status,
            2);

        header.Children.Add(
            status);

        content.Children.Add(
            header);

        // ==========================================
        // ESTADO DE AUTORIZACIÓN
        // ==========================================

        var authorization =
            new TextBlock
            {
                Text =
                    authorized
                        ? "✓ AUTORIZADO"
                        : "⚠ PENDIENTE",

                Foreground =
                    authorizationColor,

                FontSize =
                    11,

                FontWeight =
                    FontWeights.Bold,

                Margin =
                    new Thickness(
                        18,
                        7,
                        0,
                        0)
            };

        content.Children.Add(
            authorization);

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

                FontSize =
                    12,

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                Margin =
                    new Thickness(
                        0,
                        10,
                        0,
                        5)
            };

        content.Children.Add(
            hostname);

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

                FontSize =
                    10,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        content.Children.Add(
            heartbeat);

        // ==========================================
        // VERSIÓN
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

                FontSize =
                    10,

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0)
            };

        content.Children.Add(
            version);

        // ==========================================
        // ASIGNAR CONTENIDO
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