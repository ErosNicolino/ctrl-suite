using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace GraficaCaixa.Wpf;

// Per-Windows-user DPAPI. Neither the device secret nor session tokens enter the UI.
public static class ClientCredentialStore
{
 static readonly byte[] Entropy=Encoding.UTF8.GetBytes("CACP/session-device/7C");
 static string ConnectionFile(string? folder=null)=>Path.Combine(folder??AppProfile.Folder,"dispositivo7c.dat");
 static string SessionFile(ServerConnection connection)=>Path.Combine(AppProfile.Folder,"sessao7c-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connection.CertificatePin+":"+connection.DeviceId)))+".dat");
 static T? Read<T>(string path){try{if(!File.Exists(path))return default;var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser);try{return JsonSerializer.Deserialize<T>(plain);}finally{CryptographicOperations.ZeroMemory(plain);}}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException){return default;}}
 static void Write<T>(string path,T value){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var plain=JsonSerializer.SerializeToUtf8Bytes(value);byte[] encrypted;try{encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(plain);}var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,encrypted);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public static ServerConnection? LoadConnection()=>Read<ServerConnection>(ConnectionFile()) is {DeviceId:not null,Purpose:"device",Key.Length:64} c?c:null;
 public static void SaveConnection(ServerConnection connection,string? folder=null){if(connection.DeviceId==null||connection.Purpose!="device")throw new InvalidOperationException("Credencial de dispositivo esperada.");Write(ConnectionFile(folder),connection with {SelectedUrl=null});}
 public static void SaveSession(ServerConnection connection,SessionTokens tokens){if(tokens.User.DeviceId!=connection.DeviceId)throw new InvalidOperationException("Sessão pertence a outro dispositivo.");Write(SessionFile(connection),tokens);}
 public static SessionTokens? LoadSession(ServerConnection connection){var tokens=Read<SessionTokens>(SessionFile(connection));return tokens!=null&&tokens.User.DeviceId==connection.DeviceId&&tokens.RefreshExpiresAt>DateTimeOffset.UtcNow?tokens:null;}
 public static void ClearSession(ServerConnection connection){var path=SessionFile(connection);if(File.Exists(path))File.Delete(path);}
}
