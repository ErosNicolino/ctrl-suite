using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraficaCaixa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WebCustomerCommand([property:JsonRequired]Guid OperationId,[property:JsonRequired]string ExpectedRevision,
    [property:JsonRequired]string Name,[property:JsonRequired]string Contact,[property:JsonRequired]string Email);
public sealed record WebCustomerCommandResult(Guid OperationId,string Revision,bool Replayed);
public static class CustomerRevision
{
    public static string Read(SqliteConnection db,Guid id)
    {
        using var cmd=WebQueries.Command(db,"SELECT name,contact,email,cpf_cnpj,address,notes,updated_at FROM customers WHERE id=@id",("@id",id));
        using var r=cmd.ExecuteReader();if(!r.Read())throw new WebRequestException(404,"Cliente não encontrado.");
        return Hash(JsonSerializer.Serialize(Enumerable.Range(0,7).Select(i=>r.GetString(i)).ToArray()));
    }
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string From(CustomerProfile customer)=>Hash(JsonSerializer.Serialize(new[]{customer.Name,customer.Contact,customer.Email,customer.CpfCnpj,customer.Address,customer.Notes,customer.UpdatedAt.ToString("O")}));
}
public sealed partial class DataStore
{
    public Task<WebCustomerCommandResult> WebCustomerCommandAsync(UserIdentity actor,Guid id,WebCustomerCommand command,CancellationToken cancellation)
        =>Task.Run(()=>WithLock(()=>{
            cancellation.ThrowIfCancellationRequested();
            using var db=Open();var permissions=Permissions.Resolve(db,actor);
            if(!permissions.GetValueOrDefault("view.customers")||!permissions.GetValueOrDefault("customers.edit"))throw new UnauthorizedAccessException();
            if(id==Guid.Empty||command.OperationId==Guid.Empty||command.ExpectedRevision?.Length!=64||command.Name is not{Length:>0 and <=200}||string.IsNullOrWhiteSpace(command.Name)||command.Contact==null||command.Contact.Length>40||command.Email==null||command.Email.Length>254)throw new WebRequestException(400,"Confira nome, telefone e e-mail.");
            var email=command.Email.Trim();if(email.Length>0&&(!System.Net.Mail.MailAddress.TryCreate(email,out var address)||address.Address!=email))throw new WebRequestException(422,"E-mail inválido.");
            var digits=new string(command.Contact.Where(char.IsDigit).ToArray());if(command.Contact.Trim().Length>0&&(digits.Length<8||digits.Length>15))throw new WebRequestException(422,"Telefone inválido.");
            var hash=CustomerRevision.Hash(JsonSerializer.Serialize(new{contract="customer-basic-v1",id,command}));
            using(var receipt=WebQueries.Command(db,"SELECT actor_id,payload_hash FROM remote_operations WHERE id=@id",("@id",command.OperationId)))
            using(var r=receipt.ExecuteReader())if(r.Read()){
                if(r.GetString(0)!=actor.UserId.ToString()||r.GetString(1)!=hash)throw new WebRequestException(409,"Identificador de operação já utilizado.");
                r.Close();using var stored=WebQueries.Command(db,"SELECT json_extract(new_value,'$.Revision') FROM audit_trail WHERE id=@id",("@id",command.OperationId));
                return new WebCustomerCommandResult(command.OperationId,stored.ExecuteScalar()?.ToString()??throw new InvalidOperationException("Recibo ausente."),true);
            }
            if(CustomerRevision.Read(db,id)!=command.ExpectedRevision)throw new WebRequestException(409,"Este cliente foi alterado em outro dispositivo. Atualize antes de salvar.");
            var current=ReadForWrite();var next=ServerAuthorization.CloneForWrite(current);var before=current.Customers.Single(x=>x.Id==id);var customer=next.Customers.Single(x=>x.Id==id);
            try
            {
                if(command.Name.Trim()!=before.Name||PhoneText.Normalize(command.Contact)!=before.Contact)
                    CustomerBasicOperations.Edit(next,customer,command.Name,command.Contact,new OperationContext(actor.Name,actor.Role){Permissions=permissions});
                customer.Email=email;customer.UpdatedAt=DateTime.Now;
                // Granular authorization also covers all linked records changed by the established customer rule.
                ServerAuthorization.ValidateAndAudit(current,next,actor,permissions);next=Normalize(next);FinancialIntegrity.ValidateTransition(current,next);
            }
            catch(InvalidOperationException){throw new WebRequestException(422,"A regra atual do cadastro impede esta alteração. Confira duplicidade de nome ou use o desktop para clientes com pedidos pagos.");}
            var revision=CustomerRevision.From(next.Customers.Single(x=>x.Id==id));
            next.AuditTrail.Add(new(){Id=command.OperationId,Timestamp=DateTime.UtcNow,UserId=actor.UserId.ToString(),UserLogin=actor.Login,SessionId=actor.SessionId.ToString(),UserName=actor.Name,Computer="Servidor",EntityType="Cliente",EntityId=id.ToString(),Action="Cliente básico alterado",Description="Dados básicos do cliente atualizados",OldValue=JsonSerializer.Serialize(new{before.Name,before.Contact,before.Email}),NewValue=JsonSerializer.Serialize(new{customer.Name,customer.Contact,customer.Email,Revision=revision})});
            cancellation.ThrowIfCancellationRequested();CreateRoutineBackupUnsafe();SaveUnsafe(next,command.OperationId,actor.UserId.ToString(),hash);return new WebCustomerCommandResult(command.OperationId,revision,false);
        }),cancellation);
}
