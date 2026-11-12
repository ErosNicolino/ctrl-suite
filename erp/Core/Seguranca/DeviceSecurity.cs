using Microsoft.Data.Sqlite;
namespace GraficaCaixa;

public sealed record DeviceCredential(Guid Id,string Secret);
public sealed record PairDeviceRequest(string Ticket,string Name);
public sealed record PairingTicket(string Ticket,DateTimeOffset ExpiresAt);
public sealed record DeviceSummary(Guid Id,string Name,DateTimeOffset CreatedAt,DateTimeOffset? LastUsed,bool Revoked);
public sealed record SessionSummary(Guid Id,Guid UserId,string User,string Device,DateTimeOffset CreatedAt,DateTimeOffset AccessExpires,DateTimeOffset RefreshExpires,bool Revoked);
public sealed record DeviceRevoke(Guid DeviceId);
public sealed record SessionEnd(Guid SessionId);

// Extends the existing security store and audit_trail; no parallel identity system.
public sealed partial class IdentitySecurity
{
 public static readonly TimeSpan SessionMaximum=TimeSpan.FromDays(30),RefreshIdle=TimeSpan.FromDays(7);
 void MigrateDevices()=>store.SecurityWork(c=>{
  using var tx=c.BeginTransaction();
  Run(c,"""
  CREATE TABLE IF NOT EXISTS auth_devices(id TEXT PRIMARY KEY,name TEXT NOT NULL,credential_hash TEXT NOT NULL UNIQUE,created_at INTEGER NOT NULL,last_used INTEGER,revoked_at INTEGER);
  CREATE TABLE IF NOT EXISTS auth_pairings(hash TEXT PRIMARY KEY,created_by TEXT,created_at INTEGER NOT NULL,expires_at INTEGER NOT NULL,consumed_at INTEGER,revoked_at INTEGER);
  """);
  void Column(string name,string declaration){using var cmd=Command(c,"PRAGMA table_info(auth_sessions)");using var r=cmd.ExecuteReader();bool found=false;while(r.Read())if(r.GetString(1)==name)found=true;r.Close();if(!found)Run(c,$"ALTER TABLE auth_sessions ADD COLUMN {name} {declaration}");}
  Column("device_id","TEXT REFERENCES auth_devices(id)");Column("absolute_expires","INTEGER");
  tx.Commit();return 0;
 });
 DeviceCredential AddDevice(SqliteConnection c,string name,string origin,UserIdentity? actor=null){
  if(string.IsNullOrWhiteSpace(name)||name.Length>80)throw new InvalidOperationException("Nome de dispositivo inválido.");
  var credential=new DeviceCredential(Guid.NewGuid(),Token());
  Run(c,"INSERT INTO auth_devices(id,name,credential_hash,created_at) VALUES(@id,@name,@hash,@now)",("@id",credential.Id.ToString()),("@name",name.Trim()),("@hash",Hash(credential.Secret)),("@now",clock().ToUnixTimeSeconds()));
  Audit(c,"Dispositivo pareado",origin,actor,credential.Id.ToString());return credential;
 }
 // Only trusted local administration uses this; never exposed as an HTTP route.
 public DeviceCredential BootstrapDevice(string name,string origin)=>store.SecurityWork(c=>{using var tx=c.BeginTransaction();var d=AddDevice(c,name,origin);tx.Commit();return d;});
 public Guid? AuthenticateDevice(string? secret)=>secret is {Length:64}?store.ReadWork(c=>{
  using var cmd=Command(c,"SELECT id FROM auth_devices WHERE credential_hash=@h AND revoked_at IS NULL",("@h",Hash(secret)));return cmd.ExecuteScalar() is string id?(Guid?)Guid.Parse(id):null;
 }):null;
 bool DeviceActive(SqliteConnection c,Guid id){using var cmd=Command(c,"SELECT COUNT(*) FROM auth_devices WHERE id=@id AND revoked_at IS NULL",("@id",id.ToString()));return Convert.ToInt64(cmd.ExecuteScalar())==1;}
 void TouchDevice(SqliteConnection c,Guid id)=>Run(c,"UPDATE auth_devices SET last_used=@now WHERE id=@id AND (last_used IS NULL OR last_used<@older)",("@now",clock().ToUnixTimeSeconds()),("@older",clock().AddMinutes(-5).ToUnixTimeSeconds()),("@id",id.ToString()));
 public PairingTicket CreatePairing(UserIdentity actor,string origin)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");using var tx=c.BeginTransaction();var token=Token();var expiry=clock().AddMinutes(10);
  Run(c,"UPDATE auth_pairings SET revoked_at=@now WHERE created_by=@u AND consumed_at IS NULL AND revoked_at IS NULL",("@now",clock().ToUnixTimeSeconds()),("@u",actor.UserId.ToString()));
  Run(c,"INSERT INTO auth_pairings(hash,created_by,created_at,expires_at) VALUES(@h,@u,@now,@exp)",("@h",Hash(token)),("@u",actor.UserId.ToString()),("@now",clock().ToUnixTimeSeconds()),("@exp",expiry.ToUnixTimeSeconds()));
  Audit(c,"Pareamento temporário criado",origin,actor);tx.Commit();return new PairingTicket(token,expiry);
 });
 public DeviceCredential? PairDevice(PairDeviceRequest request,string origin)=>store.SecurityWork(c=>{
  if(request.Ticket is not {Length:64}||LoginThrottle.LockedFor("pair:"+origin)!=null)return null;
  using var tx=c.BeginTransaction();var hash=Hash(request.Ticket);
  using(var cmd=Command(c,"UPDATE auth_pairings SET consumed_at=@now WHERE hash=@h AND expires_at>@now AND consumed_at IS NULL AND revoked_at IS NULL",("@now",clock().ToUnixTimeSeconds()),("@h",hash)))
   if(cmd.ExecuteNonQuery()!=1){LoginThrottle.Fail("pair:"+origin);tx.Commit();return null;}
  var device=AddDevice(c,request.Name,origin);tx.Commit();return device;
 });
 public void RevokePairings(UserIdentity actor,string origin)=>store.SecurityWork(c=>{Admin(actor);Require(c,actor,"system.users");Run(c,"UPDATE auth_pairings SET revoked_at=@now WHERE consumed_at IS NULL AND revoked_at IS NULL",("@now",clock().ToUnixTimeSeconds()));Audit(c,"Códigos de pareamento revogados",origin,actor);return 0;});
 public IReadOnlyList<DeviceSummary> Devices(UserIdentity actor)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");using var cmd=Command(c,"SELECT id,name,created_at,last_used,revoked_at FROM auth_devices ORDER BY created_at DESC");using var r=cmd.ExecuteReader();var result=new List<DeviceSummary>();while(r.Read())result.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2)),r.IsDBNull(3)?null:DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)),!r.IsDBNull(4)));return (IReadOnlyList<DeviceSummary>)result;
 });
 public IReadOnlyList<SessionSummary> Sessions(UserIdentity actor)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");using var cmd=Command(c,"SELECT s.id,s.user_id,u.name,COALESCE(d.name,'Legado 7B — novo login necessário'),s.created_at,s.access_expires,s.refresh_expires,s.revoked_at FROM auth_sessions s JOIN auth_users u ON u.id=s.user_id LEFT JOIN auth_devices d ON d.id=s.device_id ORDER BY s.created_at DESC LIMIT 500");using var r=cmd.ExecuteReader();var result=new List<SessionSummary>();while(r.Read())result.Add(new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(4)),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5)),DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)),!r.IsDBNull(7)));return (IReadOnlyList<SessionSummary>)result;
 });
 public void EndSession(UserIdentity actor,Guid session,string origin)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");using var tx=c.BeginTransaction();Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE id=@id",("@id",session.ToString()),("@now",clock().ToUnixTimeSeconds()));Audit(c,"Sessão encerrada individualmente",origin,actor,session.ToString());tx.Commit();return 0;
 });
 public void RevokeDevice(UserIdentity actor,Guid device,string origin)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");using var tx=c.BeginTransaction();Run(c,"UPDATE auth_devices SET revoked_at=COALESCE(revoked_at,@now) WHERE id=@id",("@id",device.ToString()),("@now",clock().ToUnixTimeSeconds()));Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE device_id=@id",("@id",device.ToString()),("@now",clock().ToUnixTimeSeconds()));Audit(c,"Dispositivo revogado",origin,actor,device.ToString());tx.Commit();return 0;
 });
 public DeviceCredential RotateDevice(UserIdentity actor,string origin)=>store.SecurityWork(c=>{
  Admin(actor);Require(c,actor,"system.users");
  if(actor.DeviceId is not Guid device||!DeviceActive(c,device))throw new SessionInvalidException();var secret=Token();using var tx=c.BeginTransaction();Run(c,"UPDATE auth_devices SET credential_hash=@h WHERE id=@id",("@h",Hash(secret)),("@id",device.ToString()));Run(c,"UPDATE auth_sessions SET revoked_at=COALESCE(revoked_at,@now) WHERE device_id=@id",("@id",device.ToString()),("@now",clock().ToUnixTimeSeconds()));Audit(c,"Credencial de dispositivo rotacionada",origin,actor,device.ToString());tx.Commit();return new DeviceCredential(device,secret);
 });
}
