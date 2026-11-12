using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GraficaCaixa;

public sealed record LoginCheck(bool Ok, bool MustChange, string Message = "");
public sealed record LoginRequest(string User, string Password);
public sealed record PasswordChangeRequest(string User, string CurrentPassword, string NewPassword);

// Senha de acesso do ERP guardada como hash (PBKDF2, igual ao PIN do proprietário) no banco central.
// Sem senha cadastrada ninguém entra: não existe senha de fábrica no código (o servidor também exige o hash para criar o primeiro usuário).
public static class LoginSecurity
{
    public const string User = "Gráfica";
    public const int MinimumLength = 8;
    public const int PasswordIterations = 600000;

    public static bool UserMatches(string user) => string.Equals(Fold(user.Trim()), Fold(User), StringComparison.OrdinalIgnoreCase);

    public static LoginCheck Verify(Prompt12State? state, string user, string password, string origin)
    {
        if (LoginThrottle.LockedFor(origin) is TimeSpan wait)
            return new(false, false, $"Muitas tentativas erradas. Aguarde {Math.Ceiling(wait.TotalMinutes)} minuto(s) e tente de novo.");
        var credential = state?.LoginPassword;
        var ok = UserMatches(user) && credential != null && credential.Salt.Length > 0 && CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password, credential.Salt, credential.Iterations, HashAlgorithmName.SHA256, 32), credential.Hash);
        if (!ok) { LoginThrottle.Fail(origin); return new(false, false, "Usuário ou senha incorretos."); }
        LoginThrottle.Reset(origin);
        return new(true, false);
    }

    public static OwnerPinCredential Create(string password)
    {
        if (password.Length < MinimumLength) throw new InvalidOperationException($"Use uma senha com pelo menos {MinimumLength} caracteres.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return new OwnerPinCredential { Salt = salt, Iterations = PasswordIterations, Hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32) };
    }

    // Troca a senha dentro de uma alteração do banco (store.Update), já registrando na auditoria.
    public static void Change(AppData data, string user, string currentPassword, string newPassword, string origin)
    {
        var check = Verify(data.Prompt12, user, currentPassword, origin);
        if (!check.Ok) throw new UnauthorizedAccessException(check.Message.Length > 0 ? check.Message : "Senha atual incorreta.");
        data.Prompt12.LoginPassword = Create(newPassword);
        AuditService.Record(data, OperationContext.Administrator(User), "Senha de acesso alterada", "Segurança", "login", "", "", "", $"Senha de acesso do ERP alterada ({origin}).");
    }

    static string Fold(string text) => new(text.Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}

// Depois de 5 senhas erradas seguidas, bloqueia aquele computador por 2 minutos.
public static class LoginThrottle
{
    static readonly ConcurrentDictionary<string, (int Failures, DateTime Window, DateTime Until)> state = new();
    public static TimeSpan? LockedFor(string origin) => state.TryGetValue(origin, out var s) && s.Until > DateTime.UtcNow ? s.Until - DateTime.UtcNow : null;
    public static void Fail(string origin){var now=DateTime.UtcNow;if(state.Count>8192){foreach(var p in state.Where(p=>p.Value.Until<=now&&now-p.Value.Window>TimeSpan.FromMinutes(2)))state.TryRemove(p.Key,out _);if(state.Count>8192&&!state.ContainsKey(origin))return;}state.AddOrUpdate(origin,_=>(1,now,DateTime.MinValue),(_,s)=>now-s.Window>TimeSpan.FromMinutes(2)?(1,now,DateTime.MinValue):s.Failures+1>=5?(0,now,now.AddMinutes(2)):(s.Failures+1,s.Window,s.Until));}
    public static void Reset(string origin) => state.TryRemove(origin, out _);
}
