using System.Windows;
namespace GraficaCaixa.Wpf;

internal static class OwnerAuthentication
{
    internal static OwnerSession? Session { get; private set; }
    static OperationContext? user;
    static string? folder;
    static string? credentialHash;
    internal static bool Ensure(Window owner, DataStore store, bool allowSetup = false)
    {
        if(store.IsRemote)
        {
            if(Session?.IsActive==true&&ReferenceEquals(user,App.CurrentUser)&&folder==store.Folder){OwnerSecurity.Require(Session);return true;}
            Session?.Lock();Session=null;var configured=store.OwnerConfigured();
            if(!configured&&!allowSetup)throw new InvalidOperationException("Configure o PIN na Área do Proprietário.");
            var remoteDialog=new OwnerPinDialog(!configured,store){Owner=owner.IsLoaded?owner:owner.Owner};if(remoteDialog.ShowDialog()!=true)return false;            if(!remoteDialog.WasReset)store.AuthenticateOwner(remoteDialog.Pin,!configured);/* 9 min: expira antes da liberação de 10 min do servidor, para o desktop pedir o PIN de novo em vez de o servidor recusar */Session=new OwnerSession(TimeSpan.FromMinutes(9));user=App.CurrentUser;folder=store.Folder;credentialHash="servidor";return true;
        }
        var credential = store.Load().Prompt12.OwnerPin;
        var hash = credential == null ? null : Convert.ToBase64String(credential.Hash);
        if (Session?.IsActive == true && ReferenceEquals(user,App.CurrentUser) && folder == store.Folder && credentialHash == hash)
        { OwnerSecurity.Require(Session); return true; }
        Session?.Lock(); Session = null;
        if (credential == null && !allowSetup)
            throw new InvalidOperationException("Configure o PIN na Área do Proprietário antes de declarar uma perda.");
        var dialog = new OwnerPinDialog(credential == null) { Owner = owner.IsLoaded ? owner : owner.Owner };
        if (dialog.ShowDialog() != true) return false;
        if (credential == null)
        {
            credential = OwnerSecurity.Create(dialog.Pin);
            store.Update(d => {
                if(d.Prompt12.OwnerPin != null)throw new InvalidOperationException("O PIN já foi configurado. Entre novamente.");
                d.Prompt12.OwnerPin = credential;
                AuditService.Record(d,App.CurrentUser,"PIN do proprietário criado","Segurança","proprietario","","configurado","","");
                return 0;
            });
        }
        Session = OwnerSecurity.Authenticate(credential,dialog.Pin);
        user = App.CurrentUser; folder = store.Folder; credentialHash = Convert.ToBase64String(credential.Hash);
        return true;
    }
    internal static void Require(AppData data)
    {
        if(credentialHash=="servidor"&&ReferenceEquals(user,App.CurrentUser)){OwnerSecurity.Require(Session);return;}
        if(!ReferenceEquals(user,App.CurrentUser) || data.Prompt12.OwnerPin == null || credentialHash != Convert.ToBase64String(data.Prompt12.OwnerPin.Hash))
            throw new UnauthorizedAccessException("Autenticação do proprietário obrigatória.");
        OwnerSecurity.Require(Session);
    }
}
