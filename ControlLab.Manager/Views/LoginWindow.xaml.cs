using System.Windows;
using System.Windows.Input;
using ControlLab.Manager.Services;

namespace ControlLab.Manager.Views;

public partial class LoginWindow : Window
{
    private readonly AuthenticationService _authentication;

    private bool _setupMode;

    public string? SessionToken { get; private set; }

    public string? Username { get; private set; }

    public LoginWindow(
        AuthenticationService authentication)
    {
        _authentication =
            authentication;

        InitializeComponent();

        Loaded +=
            LoginWindow_Loaded;
    }

    // =========================================================
    // INICIO
    // =========================================================

    private void LoginWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _setupMode =
            !_authentication.HasAdministrator();

        ConfigureMode();

        UsernameTextBox.Focus();
    }

    // =========================================================
    // CONFIGURAR MODO
    // =========================================================

    private void ConfigureMode()
    {
        if (_setupMode)
        {
            Title =
                "ControlLab — Configuración";

            TitleText.Text =
                "Crear administrador";

            SubtitleText.Text =
                "Configura la cuenta que tendrá acceso al administrador de ControlLab.";

            LoginButton.Content =
                "CREAR ADMINISTRADOR";

            ConfirmPasswordPanel.Visibility =
                Visibility.Visible;
        }
        else
        {
            Title =
                "ControlLab — Acceso";

            TitleText.Text =
                "ControlLab";

            SubtitleText.Text =
                "Inicia sesión para administrar el laboratorio.";

            LoginButton.Content =
                "INICIAR SESIÓN";

            ConfirmPasswordPanel.Visibility =
                Visibility.Collapsed;
        }
    }

    // =========================================================
    // BOTÓN
    // =========================================================

    private void LoginButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_setupMode)
        {
            CreateAdministrator();

            return;
        }

        Login();
    }

    // =========================================================
    // CREAR ADMINISTRADOR
    // =========================================================

    private void CreateAdministrator()
    {
        ClearMessage();

        string username =
            UsernameTextBox.Text.Trim();

        string password =
            PasswordBox.Password;

        string confirmation =
            ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError(
                "Escribe un usuario."
            );

            UsernameTextBox.Focus();

            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError(
                "Escribe una contraseña."
            );

            PasswordBox.Focus();

            return;
        }

        if (password != confirmation)
        {
            ShowError(
                "Las contraseñas no coinciden."
            );

            ConfirmPasswordBox.Clear();

            ConfirmPasswordBox.Focus();

            return;
        }

        LoginButton.IsEnabled =
            false;

        try
        {
            bool created =
                _authentication.CreateAdministrator(
                    username,
                    password,
                    out string error
                );

            if (!created)
            {
                ShowError(
                    error
                );

                return;
            }

            // =================================================
            // AUTENTICAR AUTOMÁTICAMENTE
            // =================================================

            AuthenticationResult result =
                _authentication.Authenticate(
                    username,
                    password
                );

            if (!result.Succeeded)
            {
                ShowError(
                    "El administrador fue creado, pero no fue posible iniciar la sesión automáticamente."
                );

                return;
            }

            SessionToken =
                result.SessionToken;

            Username =
                result.Username;

            DialogResult =
                true;
        }
        finally
        {
            LoginButton.IsEnabled =
                true;
        }
    }

    // =========================================================
    // LOGIN
    // =========================================================

    private void Login()
    {
        ClearMessage();

        string username =
            UsernameTextBox.Text.Trim();

        string password =
            PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError(
                "Escribe tu usuario."
            );

            UsernameTextBox.Focus();

            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError(
                "Escribe tu contraseña."
            );

            PasswordBox.Focus();

            return;
        }

        LoginButton.IsEnabled =
            false;

        try
        {
            AuthenticationResult result =
                _authentication.Authenticate(
                    username,
                    password
                );

            if (!result.Succeeded)
            {
                ShowError(
                    result.Message
                );

                PasswordBox.Clear();

                PasswordBox.Focus();

                return;
            }

            SessionToken =
                result.SessionToken;

            Username =
                result.Username;

            DialogResult =
                true;
        }
        finally
        {
            LoginButton.IsEnabled =
                true;
        }
    }

    // =========================================================
    // MOSTRAR ERROR
    // =========================================================

    private void ShowError(
        string message)
    {
        MessageText.Text =
            message;

        MessageText.Foreground =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(
                    249,
                    112,
                    102
                )
            );

        MessageText.Visibility =
            Visibility.Visible;
    }

    // =========================================================
    // LIMPIAR MENSAJE
    // =========================================================

    private void ClearMessage()
    {
        MessageText.Text =
            "";

        MessageText.Visibility =
            Visibility.Collapsed;
    }

    // =========================================================
    // ENTER
    // =========================================================

    protected override void OnKeyDown(
        KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter)
        {
            LoginButton_Click(
                LoginButton,
                new RoutedEventArgs()
            );

            e.Handled =
                true;
        }
    }
}