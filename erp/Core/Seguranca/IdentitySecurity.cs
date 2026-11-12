using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GraficaCaixa;

public sealed record UserIdentity(Guid UserId, string Name, string Login, string Role, Guid SessionId){public Guid? DeviceId{get;init;}}
public sealed record SessionTokens(UserIdentity User, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, bool MustChange = false){public DateTimeOffset RefreshExpiresAt{get;init;}public DateTimeOffset AbsoluteExpiresAt{get;init;}}
public sealed record RefreshRequest(string RefreshToken);
public sealed record UserSummary(Guid Id, string Name, string Login, string Role, bool Active,DateTimeOffset? LastAccess=null);
public sealed record UserEdit(Guid? Id, string Name, string Login, string Role, bool Active, string? Password);
public sealed record OwnerPinRequest(string Pin, bool Setup = false);
public sealed record OwnerPinReset(string Password, string NewPin);

// Opaque access/refresh tokens: all identity and expiry data stays on the server.
// Only SHA-256 digests of 256-bit random tokens are persisted; no signing secret is needed.
public sealed partial class IdentitySecurity
{
    readonly DataStore store;
    readonly Func<DateTimeOffset> clock;
    readonly TimeSpan accessLifetime;
    const string InvalidCredentials = "Usuário ou senha inválidos.";
    static readonly OwnerPinCredential Dummy = LoginSecurity.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    public static readonly string[] Roles = ["Administrador", "Financeiro"];
    public IdentitySecurity(DataStore store, Func<DateTimeOffset>? clock = null, TimeSpan? accessLifetime = null)
    { this.store=store; this.clock=clock??(()=>DateTimeOffset.UtcNow); this.accessLifetime=accessLifetime??TimeSpan.FromMinutes(15); }
    static string Normalize(string login) => new string(login.Trim().Normalize(NormalizationForm.FormD).Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
    static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    static SqliteCommand Command(SqliteConnection c, string sql, params (string,object?)[] values)
    { var cmd=c.CreateCommand();cmd.CommandText=sql;foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value??DBNull.Value);return cmd; }
    static void Run(SqliteConnection c,string sql,params (string,object?)[] values){using var cmd=Command(c,sql,values);cmd.ExecuteNonQuery();}
    static void Audit(SqliteConnection c,string action,string origin,UserIdentity? user=null, string? entity=null)
    {
        Run(c,"INSERT INTO audit_trail(id,timestamp,action,description,computer,user_name,entity_type,entity_id,old_value,new_value,reason,user_id,user_login,session_id) VALUES(@id,@time,@action,@description,@origin,@name,'Segurança',@entity,'','','',@user,@login,@session)",
            ("@id",Guid.NewGuid().ToString()),("@time",DateTime.UtcNow.ToString("O")),("@action",action),("@description",user==null?"Autenticação recusada":$"{user.Name} · {user.Login}"),("@origin",origin),("@name",user?.Name??"Não autenticado"),("@entity",entity??user?.UserId.ToString()??"login"),("@user",user?.UserId.ToString()??""),("@login",user?.Login??""),("@session",user?.SessionId.ToString()??""));
    }
    public void Migrate() { store.SecurityWork(c=>
    {
        using var tx=c.BeginTransaction();
        Run(c,"""
        CREATE TABLE IF NOT EXISTS auth_users(id TEXT PRIMARY KEY,name TEXT NOT NULL,login TEXT NOT NULL,normalized_login TEXT NOT NULL UNIQUE,salt BLOB NOT NULL,password_hash BLOB NOT NULL,iterations INTEGER NOT NULL,active INTEGER NOT NULL,role TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,must_change INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS auth_sessions(id TEXT PRIMARY KEY,user_id TEXT NOT NULL REFERENCES auth_users(id),access_hash TEXT NOT NULL UNIQUE,access_expires INTEGER NOT NULL,refresh_hash TEXT NOT NULL UNIQUE,refresh_expires INTEGER NOT NULL,created_at INTEGER NOT NULL,revoked_at INTEGER,owner_until INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS ix_auth_sessions_user ON auth_sessions(user_id);
        CREATE TABLE IF NOT EXISTS auth_used_refresh(hash TEXT PRIMARY KEY,session_id TEXT NOT NULL REFERENCES auth_sessions(id));
        """);
        using var count=Command(c,"SELECT COUNT(*) FROM auth_users");
        if(Convert.ToInt64(count.ExecuteScalar())==0)
        {
            using var read=Command(c,"SELECT value FROM metadata WHERE key='prompt12_v1'");
            var prompt=JsonSerializer.Deserialize<Prompt12State>(read.ExecuteScalar()?.ToString()??"{}")!;
            var credential=prompt.LoginPassword;
            if(credential==null || credential.Hash.Length!=32 || credential.Salt.Length<16 || credential.Iterations<100000)
                throw new InvalidOperationException("A migração exige o hash PBKDF2 existente. Configure a senha no ERP atual antes de ativar a fase 7A.");
            var now=clock().ToString("O");
            Run(c,"INSERT INTO auth_users VALUES(@id,@name,@login,@normalized,@salt,@hash,@iterations,1,'Administrador',@now,@now,0)",("@id",Guid.NewGuid().ToString()),("@name",LoginSecurity.User),("@login",LoginSecurity.User),("@normalized",Normalize(LoginSecurity.User)),("@salt",credential.Salt),("@hash",credential.Hash),("@iterations",credential.Iterations),("@now",now));
        }
        tx.Commit();return 0;
    }); MigratePermissions(); MigrateDevices(); }
    public SessionTokens? Login(string login,string password,string origin,Guid? deviceId=null) => store.SecurityWork(c=>
    {
        if(login is not {Length:>0 and <=80}||password is not {Length:<=1024})return null;
        if(deviceId.HasValue&&!DeviceActive(c,deviceId.Value))return null;
        var accountKey="account:"+Hash(Normalize(login??"")+":"+(deviceId?.ToString()??origin));
        if(LoginThrottle.LockedFor(origin)!=null||LoginThrottle.LockedFor(accountKey)!=null){Audit(c,"Login recusado",origin);return null;}
        using var cmd=Command(c,"SELECT id,name,login,role,salt,password_hash,iterations,active FROM auth_users WHERE normalized_login=@login",("@login",Normalize(login??"")));
        UserIdentity? user=null;OwnerPinCredential credential=Dummy;bool active=false;
        using(var r=cmd.ExecuteReader())if(r.Read())
        {user=new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),Guid.NewGuid());credential=new(){Salt=(byte[])r[4],Hash=(byte[])r[5],Iterations=r.GetInt32(6)};active=r.GetInt32(7)==1;}
        var valid=Verify(credential,password??"");
        if(!valid||!active||user==null){LoginThrottle.Fail(origin);LoginThrottle.Fail(accountKey);Audit(c,"Login recusado",origin);return null;}
        LoginThrottle.Reset(origin);LoginThrottle.Reset(accountKey);
        using var tx=c.BeginTransaction();
        user=user with {DeviceId=deviceId};var tokens=Issue(c,user,true);if(deviceId.HasValue)TouchDevice(c,deviceId.Value);Audit(c,"Login bem-sucedido",origin,user);tx.Commit();return tokens;
    });
    static bool Verify(OwnerPinCredential credential,string password) => password.Length<=1024 && credential.Salt.Length>=16 && credential.Hash.Length==32 && credential.Iterations is >=100000 and <=2000000 && CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(password,credential.Salt,credential.Iterations,HashAlgorithmName.SHA256,32),credential.Hash);
    SessionTokens Issue(SqliteConnection c,UserIdentity user,bool create)
    {
        var access=Token();var refresh=Token();var now=clock();var absolute=now.Add(SessionMaximum);
        if(!create){using var read=Command(c,"SELECT COALESCE(absolute_expires,refresh_expires) FROM auth_sessions WHERE id=@s",("@s",user.SessionId.ToString()));absolute=DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(read.ExecuteScalar()));}
        var expiry=now.Add(accessLifetime);if(expiry>absolute)expiry=absolute;var refreshExpiry=now.Add(RefreshIdle);if(refreshExpiry>absolute)refreshExpiry=absolute;
        if(create)Run(c,"INSERT INTO auth_sessions(id,user_id,access_hash,access_expires,refresh_hash,refresh_expires,created_at,device_id,absolute_expires) VALUES(@sid,@uid,@access,@expires,@refresh,@refreshExpires,@now,@device,@absolute)",("@sid",user.SessionId.ToString()),("@uid",user.UserId.ToString()),("@access",Hash(access)),("@expires",expiry.ToUnixTimeSeconds()),("@refresh",Hash(refresh)),("@refreshExpires",refreshExpiry.ToUnixTimeSeconds()),("@now",now.ToUnixTimeSeconds()),("@device",user.DeviceId?.ToString()),("@absolute",absolute.ToUnixTimeSeconds()));
        else Run(c,"UPDATE auth_sessions SET access_hash=@access,access_expires=@expires,refresh_hash=@refresh,refresh_expires=@refreshExpires WHERE id=@sid",("@sid",user.SessionId.ToString()),("@access",Hash(access)),("@expires",expiry.ToUnixTimeSeconds()),("@refresh",Hash(refresh)),("@refreshExpires",refreshExpiry.ToUnixTimeSeconds()));
        return new SessionTokens(user,access,refresh,expiry){RefreshExpiresAt=refreshExpiry,AbsoluteExpiresAt=absolute};
    }
    UserIdentity? Find(SqliteConnection c,string hash,bool refresh,Guid? deviceId=null)
    {
        using var cmd=Command(c,$"SELECT u.id,u.name,u.login,u.role,s.id,s.device_id FROM auth_sessions s JOIN auth_users u ON u.id=s.user_id LEFT JOIN auth_devices d ON d.id=s.device_id WHERE s.{(refresh?"refresh_hash":"access_hash")}=@hash AND s.revoked_at IS NULL AND u.active=1 AND s.{(refresh?"refresh_expires":"access_expires")}>@now AND COALESCE(s.absolute_expires,s.refresh_expires)>@now AND (s.device_id IS NULL OR (d.id IS NOT NULL AND d.revoked_at IS NULL)) AND (@device IS NULL OR s.device_id=@device)",("@hash",hash),("@now",clock().ToUnixTimeSeconds()),("@device",deviceId?.ToString()));
        using var r=cmd.ExecuteReader();return r.Read()?new UserIdentity(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),Guid.Parse(r.GetString(4))){DeviceId=r.IsDBNull(5)?null:Guid.Parse(r.GetString(5))}:null;
    }
    public UserIdentity? Authenticate(string token,Guid? deviceId=null) => token is {Length:64}?store.ReadWork(c=>Find(c,Hash(token),false,deviceId)):null;
    public SessionTokens? Refresh(string token,string origin,Guid? deviceId=null) => store.SecurityWork(c=>
    {
        if(token==null||token.Length!=64)return null;
        var hash=Hash(token);using var tx=c.BeginTransaction();var user=Find(c,hash,true,deviceId);
        if(user==null)
        {
            using var replay=Command(c,"SELECT r.session_id FROM auth_used_refresh r JOIN auth_sessions s ON s.id=r.session_id WHERE r.hash=@hash AND (@device IS NULL OR s.device_id=@device)",("@hash",hash),("@device",deviceId?.ToString()));
            if(replay.ExecuteScalar() is string sid){Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE id=@sid",("@now",clock().ToUnixTimeSeconds()),("@sid",sid));Audit(c,"Sessão revogada",origin,entity:sid);}
            tx.Commit();return null;
        }
        Run(c,"INSERT INTO auth_used_refresh VALUES(@hash,@sid)",("@hash",hash),("@sid",user.SessionId.ToString()));
        var result=Issue(c,user,false);if(user.DeviceId.HasValue)TouchDevice(c,user.DeviceId.Value);tx.Commit();return result;
    });
    public void Logout(UserIdentity user,string origin) => store.SecurityWork(c=>{using var tx=c.BeginTransaction();Run(c,"UPDATE auth_sessions SET revoked_at=@now WHERE id=@sid",("@now",clock().ToUnixTimeSeconds()),("@sid",user.SessionId.ToString()));Audit(c,"Logout",origin,user);Audit(c,"Sessão revogada",origin,user);tx.Commit();return 0;});
    public IReadOnlyList<UserSummary> Users(UserIdentity actor) => store.SecurityWork(c=>
    {Require(c,actor,"system.users");using var cmd=Command(c,"SELECT u.id,u.name,u.login,u.role,u.active,(SELECT MAX(created_at) FROM auth_sessions s WHERE s.user_id=u.id) FROM auth_users u ORDER BY u.name");using var r=cmd.ExecuteReader();var users=new List<UserSummary>();while(r.Read())users.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)==1,r.IsDBNull(5)?null:DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5))));return (IReadOnlyList<UserSummary>)users;});
    static void Admin(UserIdentity actor){if(actor.Role!="Administrador")throw new UnauthorizedAccessException("Perfil Administrador obrigatório.");}
    public void SaveUser(UserIdentity actor,UserEdit edit,string origin) => store.SecurityWork(c=>
    {
        Require(c,actor,"system.users");if(string.IsNullOrWhiteSpace(edit.Name)||edit.Name.Length>120||string.IsNullOrWhiteSpace(edit.Login)||edit.Login.Length>80||!RoleNames(c).Contains(edit.Role))throw new InvalidOperationException("Nome, login ou perfil inválidos.");
        if(edit.Password is {Length:>1024})throw new InvalidOperationException("Senha muito longa.");
        using var tx=c.BeginTransaction();var id=edit.Id??Guid.NewGuid();var now=clock().ToString("O");
        UserSummary? previous=null;
        if(edit.Id.HasValue)using(var read=Command(c,"SELECT id,name,login,role,active FROM auth_users WHERE id=@id",("@id",id.ToString())))using(var r=read.ExecuteReader())if(r.Read())previous=new(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)==1);
        if(edit.Id==null)
        {
            var credential=LoginSecurity.Create(edit.Password??"");
            Run(c,"INSERT INTO auth_users VALUES(@id,@name,@login,@normalized,@salt,@hash,@iterations,@active,@role,@now,@now,0)",("@id",id.ToString()),("@name",edit.Name.Trim()),("@login",edit.Login.Trim()),("@normalized",Normalize(edit.Login)),("@salt",credential.Salt),("@hash",credential.Hash),("@iterations",credential.Iterations),("@active",edit.Active?1:0),("@role",edit.Role),("@now",now));
        }
        else
        {
            using var exists=Command(c,"SELECT role FROM auth_users WHERE id=@id",("@id",id.ToString()));if(exists.ExecuteScalar()==null)throw new InvalidOperationException("Usuário não encontrado.");
            using var admins=Command(c,"SELECT COUNT(*) FROM auth_users WHERE active=1 AND role='Administrador' AND id<>@id",("@id",id.ToString()));if((!edit.Active||edit.Role!="Administrador")&&Convert.ToInt64(admins.ExecuteScalar())==0)throw new InvalidOperationException("Preserve pelo menos um Administrador ativo.");
            Run(c,"UPDATE auth_users SET name=@name,login=@login,normalized_login=@normalized,role=@role,active=@active,updated_at=@now WHERE id=@id",("@id",id.ToString()),("@name",edit.Name.Trim()),("@login",edit.Login.Trim()),("@normalized",Normalize(edit.Login)),("@role",edit.Role),("@active",edit.Active?1:0),("@now",now));
            if(!string.IsNullOrEmpty(edit.Password)){var credential=LoginSecurity.Create(edit.Password);Run(c,"UPDATE auth_users SET salt=@salt,password_hash=@hash,iterations=@iterations WHERE id=@id",("@salt",credential.Salt),("@hash",credential.Hash),("@iterations",credential.Iterations),("@id",id.ToString()));}
            Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE user_id=@id",("@now",clock().ToUnixTimeSeconds()),("@id",id.ToString()));Audit(c,"Sessões revogadas por alteração de usuário",origin,actor,id.ToString());
        }
        EnsureManager(c);Audit(c,!edit.Active?"Usuário desativado":edit.Id==null?"Usuário criado":!string.IsNullOrEmpty(edit.Password)?"Senha redefinida":"Usuário atualizado",origin,actor,id.ToString());
        Run(c,"UPDATE audit_trail SET old_value=@old,new_value=@value,description=@description WHERE rowid=last_insert_rowid()",("@old",previous==null?"":JsonSerializer.Serialize(previous)),("@value",JsonSerializer.Serialize(new UserSummary(id,edit.Name.Trim(),edit.Login.Trim(),edit.Role,edit.Active))),("@description",$"{edit.Name.Trim()} · {edit.Login.Trim()} · {edit.Role} · {(edit.Active?"Ativo":"Inativo")}"));tx.Commit();return 0;
    });
    public void ChangePassword(UserIdentity actor,string current,string next,string origin) => store.SecurityWork(c=>
    {
        if(next.Length>1024)throw new InvalidOperationException("Senha muito longa.");
        var key="password:"+actor.UserId; if(LoginThrottle.LockedFor(key)!=null)throw new UnauthorizedAccessException(InvalidCredentials);
        using var cmd=Command(c,"SELECT salt,password_hash,iterations FROM auth_users WHERE id=@id",("@id",actor.UserId.ToString()));OwnerPinCredential credential;
        using(var r=cmd.ExecuteReader()){if(!r.Read())throw new UnauthorizedAccessException(InvalidCredentials);credential=new(){Salt=(byte[])r[0],Hash=(byte[])r[1],Iterations=r.GetInt32(2)};}
        if(!Verify(credential,current)){LoginThrottle.Fail(key);throw new UnauthorizedAccessException(InvalidCredentials);}LoginThrottle.Reset(key);
        var fresh=LoginSecurity.Create(next);using var tx=c.BeginTransaction();Run(c,"UPDATE auth_users SET salt=@salt,password_hash=@hash,iterations=@iterations,updated_at=@now WHERE id=@id",("@salt",fresh.Salt),("@hash",fresh.Hash),("@iterations",fresh.Iterations),("@now",clock().ToString("O")),("@id",actor.UserId.ToString()));
        // Keep legacy metadata synchronized for binary rollback, using the original migrated login.
        if(LoginSecurity.UserMatches(actor.Login)){using var read=Command(c,"SELECT value FROM metadata WHERE key='prompt12_v1'");var p=JsonSerializer.Deserialize<Prompt12State>(read.ExecuteScalar()?.ToString()??"{}")!;p.LoginPassword=fresh;Run(c,"UPDATE metadata SET value=@v WHERE key='prompt12_v1'",("@v",JsonSerializer.Serialize(p)));}
        Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE user_id=@uid AND id<>@sid",("@now",clock().ToUnixTimeSeconds()),("@uid",actor.UserId.ToString()),("@sid",actor.SessionId.ToString()));Audit(c,"Senha alterada; outras sessões revogadas",origin,actor);tx.Commit();return 0;
    });
    public bool OwnerConfigured() => store.SecurityWork(c=>{using var cmd=Command(c,"SELECT value FROM metadata WHERE key='prompt12_v1'");return JsonSerializer.Deserialize<Prompt12State>(cmd.ExecuteScalar()?.ToString()??"{}")?.OwnerPin!=null;});
    // "Esqueci o PIN": só Administrador, confirmando a própria senha de login (mesma trava de tentativas da troca de senha).
    // O PIN antigo deixa de valer, as outras áreas do proprietário abertas são fechadas e a troca fica na auditoria.
    public void ResetOwnerPin(UserIdentity user,OwnerPinReset request,string origin) => store.SecurityWork(c=>
    {
        Admin(user);
        if(request.Password is not {Length:>0 and <=1024}||request.NewPin is not {Length:>=6 and <=32}||!request.NewPin.All(char.IsAsciiDigit))throw new InvalidOperationException("Use um PIN numérico com pelo menos 6 dígitos.");
        var key="password:"+user.UserId;if(LoginThrottle.LockedFor(key)!=null)throw new UnauthorizedAccessException(InvalidCredentials);
        using var read=Command(c,"SELECT salt,password_hash,iterations FROM auth_users WHERE id=@id AND active=1",("@id",user.UserId.ToString()));OwnerPinCredential credential;
        using(var r=read.ExecuteReader()){if(!r.Read())throw new UnauthorizedAccessException(InvalidCredentials);credential=new(){Salt=(byte[])r[0],Hash=(byte[])r[1],Iterations=r.GetInt32(2)};}
        if(!Verify(credential,request.Password)){LoginThrottle.Fail(key);Audit(c,"Redefinição do PIN recusada",origin,user);throw new UnauthorizedAccessException("Senha de login incorreta. O PIN não foi alterado.");}
        LoginThrottle.Reset(key);
        using var cmd=Command(c,"SELECT value FROM metadata WHERE key='prompt12_v1'");var p=JsonSerializer.Deserialize<Prompt12State>(cmd.ExecuteScalar()?.ToString()??"{}")!;
        using var tx=c.BeginTransaction();
        p.OwnerPin=OwnerSecurity.Create(request.NewPin);Run(c,"UPDATE metadata SET value=@v WHERE key='prompt12_v1'",("@v",JsonSerializer.Serialize(p)));
        Run(c,"UPDATE auth_sessions SET owner_until=0 WHERE id<>@sid",("@sid",user.SessionId.ToString()));
        Run(c,"UPDATE auth_sessions SET owner_until=@until WHERE id=@sid",("@until",clock().AddMinutes(10).ToUnixTimeSeconds()),("@sid",user.SessionId.ToString()));
        Audit(c,"PIN do proprietário redefinido",origin,user);tx.Commit();return 0;
    });
    public void OwnerAuthenticate(UserIdentity user,OwnerPinRequest request,string origin) => store.SecurityWork(c=>
    {
        var key="owner:"+user.UserId;if(LoginThrottle.LockedFor(key)!=null)throw new UnauthorizedAccessException("PIN inválido.");
        using var cmd=Command(c,"SELECT value FROM metadata WHERE key='prompt12_v1'");var p=JsonSerializer.Deserialize<Prompt12State>(cmd.ExecuteScalar()?.ToString()??"{}")!;
        using var tx=c.BeginTransaction();
        if(p.OwnerPin==null){Admin(user);if(!request.Setup)throw new InvalidOperationException("Configure o PIN do proprietário.");p.OwnerPin=OwnerSecurity.Create(request.Pin);Run(c,"UPDATE metadata SET value=@v WHERE key='prompt12_v1'",("@v",JsonSerializer.Serialize(p)));Audit(c,"PIN do proprietário criado",origin,user);}
        else if(!Verify(p.OwnerPin,request.Pin)){LoginThrottle.Fail(key);throw new UnauthorizedAccessException("PIN inválido.");}
        LoginThrottle.Reset(key);Run(c,"UPDATE auth_sessions SET owner_until=@until WHERE id=@sid",("@until",clock().AddMinutes(10).ToUnixTimeSeconds()),("@sid",user.SessionId.ToString()));tx.Commit();return 0;
    });
}



