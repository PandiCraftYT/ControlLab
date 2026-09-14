using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using ControlLab.Manager.Controls;
using ControlLab.Manager.Models;
using ControlLab.Manager.Services;
using ControlLab.Manager.Views;

namespace ControlLab.Manager;

public partial class MainWindow : Window
{
    // ==========================================
    // API
    // ==========================================

    private readonly ControlLabApi _api = new();

    // ==========================================
    // ACTUALIZACIÓN AUTOMÁTICA
    // ==========================================

    private readonly DispatcherTimer _refreshTimer;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainWindow()
    {
        InitializeComponent();

        // ==========================================
        // TIMER
        // ==========================================

        _refreshTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(3)
            };

        _refreshTimer.Tick +=
            async (_, _) =>
                await LoadAgentsAsync();

        // ==========================================
        // CARGA INICIAL
        // ==========================================

        Loaded +=
            async (_, _) =>
                await LoadAgentsAsync();

        // ==========================================
        // INICIAR ACTUALIZACIÓN
        // ==========================================

        _refreshTimer.Start();
    }

    // ==========================================
    // CARGAR EQUIPOS
    // ==========================================

    private async Task LoadAgentsAsync()
    {
        try
        {
            // ==========================================
            // CONSULTAR SERVIDOR
            // ==========================================

            var data =
                await _api.GetAgentsAsync();

            if (data == null)
                return;

            // ==========================================
            // LIMPIAR LISTA ACTUAL
            // ==========================================

            AgentsPanel.Children.Clear();

            // ==========================================
            // OBTENER EQUIPOS
            // ==========================================

            var agents =
                data.Agents
                    .OrderBy(
                        agent =>
                            agent.MachineId,
                        StringComparer
                            .OrdinalIgnoreCase
                    )
                    .ToList();

            // ==========================================
            // CREAR TARJETAS
            // ==========================================

            foreach (
                var agent
                in agents
            )
            {
                AgentsPanel.Children.Add(
                    CreateAgentCard(
                        agent
                    )
                );
            }

            // ==========================================
            // ESTADÍSTICAS
            // ==========================================

            int total =
                agents.Count;

            int online =
                agents.Count(
                    agent =>
                        agent.Status.Equals(
                            "online",
                            StringComparison
                                .OrdinalIgnoreCase
                        )
                );

            int offline =
                agents.Count(
                    agent =>
                        agent.Status.Equals(
                            "offline",
                            StringComparison
                                .OrdinalIgnoreCase
                        )
                );

            // ==========================================
            // ACTUALIZAR CONTADORES
            // ==========================================

            TotalEquiposText.Text =
                total.ToString();

            EquiposOnlineText.Text =
                online.ToString();

            EquiposOfflineText.Text =
                offline.ToString();

            // ==========================================
            // PIE DE PÁGINA
            // ==========================================

            FooterEquiposText.Text =
                total == 1
                    ? "1 equipo registrado"
                    : $"{total} equipos registrados";
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error consultando servidor: {ex.Message}"
            );
        }
    }

    // ==========================================
    // CREAR TARJETA
    // ==========================================

    private Border CreateAgentCard(
        AgentInfo agent)
    {
        return AgentCard.Create(
            agent.MachineId,
            agent,
            () =>
            {
                ShowAgentDetails(
                    agent.MachineId,
                    agent
                );
            }
        );
    }

    // ==========================================
    // DETALLES DEL EQUIPO
    // ==========================================

    private void ShowAgentDetails(
        string machineId,
        AgentInfo agent)
    {
        var window =
            new AgentDetailsWindow(
                machineId,
                agent,
                _api
            )
            {
                Owner = this
            };

        window.ShowDialog();
    }

    // ==========================================
    // CERRAR
    // ==========================================

    protected override void OnClosed(
        EventArgs e)
    {
        _refreshTimer.Stop();

        _api.Dispose();

        base.OnClosed(e);
    }
}