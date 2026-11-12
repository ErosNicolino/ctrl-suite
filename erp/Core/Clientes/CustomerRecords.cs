using System.Runtime.CompilerServices;
namespace GraficaCaixa;

// De qual cadastro é cada pedido e lançamento, para os totais de Clientes e a ficha. Antes somava só pelo nome: duas Adrianas
// com telefones diferentes mostravam a soma das duas. Ordem: vínculo do pedido; telefone completo (é o que identifica o cliente,
// mesmo com o nome escrito diferente ou de um cadastro mesclado); nome, só quando não deixa dúvida. Na dúvida, fica sem dono.
public sealed class CustomerRecords
{
    readonly HashSet<Guid> ids=[];
    readonly Dictionary<string,List<CustomerProfile>> byPhone=[];
    readonly Dictionary<string,List<CustomerProfile>> byName=new(StringComparer.CurrentCultureIgnoreCase);
    static readonly ConditionalWeakTable<AppData,CustomerRecords> cached=new();
    public static CustomerRecords For(AppData data)=>cached.GetValue(data,x=>new CustomerRecords(x.Customers));
    public CustomerRecords(IEnumerable<CustomerProfile> customers)
    {
        foreach(var customer in customers)
        {
            ids.Add(customer.Id);
            var key=CustomerBasicOperations.PhoneKey(customer.Contact);if(key.Length>0)Add(byPhone,key,customer);
            foreach(var name in customer.AllNames())Add(byName,name,customer);
        }
    }
    static void Add(Dictionary<string,List<CustomerProfile>> index,string key,CustomerProfile customer){if(!index.TryGetValue(key,out var list))index[key]=list=[];if(!list.Contains(customer))list.Add(customer);}
    public Guid? Owner(Guid? customerId,string? name,string? contact)
    {
        if(customerId is Guid id&&ids.Contains(id))return id;
        var clean=CustomerBasicOperations.Clean(name);
        List<CustomerProfile> named=CustomerBasicOperations.IsGeneric(clean)||!byName.TryGetValue(clean,out var found)?[]:found;
        var key=CustomerBasicOperations.PhoneKey(contact);
        if(key.Length>0&&byPhone.TryGetValue(key,out var sharing))
        {
            if(sharing.Count==1)return sharing[0].Id;
            // Mesmo telefone em mais de um cadastro: decide pelo nome.
            var same=sharing.Where(named.Contains).ToList();return same.Count==1?same[0].Id:null;
        }
        // Telefone completo que não é de ninguém: é outra pessoa, a não ser que o cadastro de mesmo nome ainda não tenha telefone.
        var candidates=key.Length>0?named.Where(x=>CustomerBasicOperations.PhoneKey(x.Contact).Length==0).ToList():named;
        return candidates.Count==1?candidates[0].Id:null;
    }
    public Guid? Owner(ScheduledOrder order)=>Owner(order.CustomerId,order.CustomerName,order.Contact);
    public Guid? Owner(Sale sale)=>Owner(null,sale.CustomerName,sale.Contact);
}
