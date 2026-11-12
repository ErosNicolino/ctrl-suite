using System.Text.Json;
namespace GraficaCaixa;
public sealed record ReceiptAuditRequest(Guid Id,string Payer,decimal Amount,string PaymentType,string Reference);
public sealed partial class IdentitySecurity
{
    public void AuthorizeReceipt(UserIdentity actor,ReceiptAuditRequest receipt,string origin)=>store.SecurityWork(c=>
    {
        Require(c,actor,"finance.receipt");if(receipt.Id==Guid.Empty||string.IsNullOrWhiteSpace(receipt.Payer)||receipt.Payer.Length>180||string.IsNullOrWhiteSpace(receipt.Reference)||receipt.Reference.Length>2000||receipt.Amount<=0||decimal.Round(receipt.Amount,2)!=receipt.Amount||!PaymentPolicy.IsReceiptPaymentType(receipt.PaymentType))throw new InvalidOperationException("Recibo inválido.");
        using var exists=Command(c,"SELECT COUNT(*) FROM audit_trail WHERE id=@id",("@id",receipt.Id.ToString()));if(Convert.ToInt32(exists.ExecuteScalar())>0)return 0;
        Run(c,"INSERT INTO audit_trail(id,timestamp,action,description,computer,user_name,user_id,user_login,session_id,entity_type,entity_id,old_value,new_value,reason) VALUES(@id,@time,'Recibo autorizado',@description,@origin,@name,@user,@login,@session,'Recibo',@id,'',@value,'')",("@id",receipt.Id.ToString()),("@time",DateTime.UtcNow.ToString("O")),("@description",$"{receipt.Payer}: {receipt.Amount:C2} via {receipt.PaymentType}"),("@origin",origin),("@name",actor.Name),("@user",actor.UserId.ToString()),("@login",actor.Login),("@session",actor.SessionId.ToString()),("@value",JsonSerializer.Serialize(receipt)));return 0;
    });
}
