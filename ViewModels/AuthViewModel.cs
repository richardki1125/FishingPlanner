using FishingPlanner.Models;
using FishingPlanner.Services;
using Microsoft.Extensions.Logging;

namespace FishingPlanner.ViewModels;

/// <summary>
/// A felhasználónév + jelszó alapú bejelentkezés és regisztráció ViewModel-je.
/// </summary>
public sealed class AuthViewModel : ViewModelBase
{
    private readonly IAuthService _authService;

    public bool IsRegisterMode { get; private set; }
    public LoginModel LoginForm { get; private set; } = new();
    public RegisterModel RegisterForm { get; private set; } = new();

    public AuthViewModel(
        IAuthService authService,
        ILogger<AuthViewModel> logger)
        : base(logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public void ToggleAuthMode(bool registerMode)
    {
        ClearMessages();
        IsRegisterMode = registerMode;
        NotifyStateChanged();
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        var ok = await ExecuteAsync(async () =>
        {
            var result = await _authService.SignInAsync(LoginForm.Felhasznalonev, LoginForm.Password, ct);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Sikertelen bejelentkezés.");
            }
        }, "Nem sikerült bejelentkezni.");

        if (ok)
        {
            LoginForm.Password = string.Empty;
        }
    }

    public async Task RegisterAsync(CancellationToken ct = default)
    {
        var ok = await ExecuteAsync(async () =>
        {
            var result = await _authService.SignUpAsync(
                RegisterForm.Felhasznalonev,
                RegisterForm.Password,
                ct);

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Sikertelen regisztráció.");
            }
        }, "Nem sikerült létrehozni a fiókot.");

        if (ok)
        {
            RegisterForm.Password = string.Empty;
            RegisterForm.PasswordConfirm = string.Empty;
        }
    }
}
