using System.Text.Json;
namespace GraficaCaixa;

public static class CustomerBasicOperations
{
    // Shared with CustomerDirectory: retain the established association-by-name rule.
    public static void Edit(AppData data,CustomerProfile original,string name,string contact,OperationContext context)
    {
        name=Clean(name);contact=PhoneText.Normalize(Clean(contact));
        // Cadastro antigo sem nome: pode corrigir só o telefone; o nome continua "Não informado".
        if(name.Length==0)name="Não informado";
        var customer=data.Customers.FirstOrDefault(x=>x.Id==original.Id)??throw new InvalidOperationException("Cliente não encontrado. Atualize a lista.");
        DataRelations.RequireUnchanged(customer,original);
        // Só é duplicado de verdade se outro cadastro tiver o mesmo nome E o mesmo telefone.
        // (Antes bastava outro cliente com o nome antigo para travar a edição dos cadastros antigos.)
        var digits=PhoneText.Digits(contact);
        // Nomes iguais são normais (dois Ricardos); só recusa o mesmo nome com o mesmo telefone preenchido.
        if(digits.Length>0&&data.Customers.Any(x=>x.Id!=customer.Id&&x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)&&PhoneText.Digits(Clean(x.Contact))==digits))
            throw new InvalidOperationException("Já existe outro cliente com esse nome e esse telefone. Exclua o cadastro repetido em vez de criar outro igual.");
        // Telefone trocado para um número que já é de outro cliente: recusado (os repetidos antigos se resolvem com Mesclar).
        if(PhoneText.Digits(contact)!=PhoneText.Digits(Clean(original.Contact))&&!PhoneText.IsCompleteOrEmpty(contact))throw new InvalidOperationException(PhoneText.IncompleteMessage);
        if(PhoneKey(contact)!=PhoneKey(original.Contact)&&PhoneOwner(data,contact,customer.Id) is {} owner)throw new InvalidOperationException(PhoneTaken(owner));
        // Lançamentos do caixa aberto só são corrigidos quando são deste cliente (mesmo nome e mesmo telefone ou sem telefone);
        // cadastro sem nome ("Não informado", "Cliente Balcão") nunca renomeia lançamentos de outras pessoas.
        var originalDigits=PhoneText.Digits(Clean(original.Contact));var generic=IsGeneric(original.Name);
        bool Matches(Sale sale)=>!generic&&sale.CustomerName.Equals(original.Name,StringComparison.CurrentCultureIgnoreCase)&&(originalDigits.Length==0||PhoneText.Digits(sale.Contact) is var d&&(d.Length==0||d==originalDigits));
        bool MatchesName(string candidate)=>!generic&&candidate.Equals(original.Name,StringComparison.CurrentCultureIgnoreCase);
        foreach(var order in data.ScheduledOrders.Where(x=>ReceivableViews.Customer(data,x.CustomerId,x.CustomerName,x.Contact)?.Id==customer.Id)){order.CustomerId=customer.Id;order.Revision++;}
        foreach(var sale in data.Sessions.SelectMany(x=>x.Sales).Where(Matches).ToList())
        {
            if(!data.Sessions.First(x=>x.Sales.Contains(sale)).IsOpen)continue;
            var draft=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(sale))!;draft.CustomerName=name;draft.Contact=contact;
            draft.TotalValue=data.FinancialCorrections.Where(x=>x.OriginalSaleId==sale.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??sale.Total;
            FinancialOperations.EditSale(data,sale,draft,"Correção no cadastro do cliente",context);
        }
        foreach(var item in data.ManualDebts.Where(x=>ReceivableViews.Customer(data,x.CustomerId,x.CustomerName,x.Contact)?.Id==customer.Id))item.CustomerId=customer.Id;
        foreach(var item in data.Prospects.Where(x=>MatchesName(x.CustomerName))){item.CustomerName=name;item.Contact=contact;}
        customer.Name=name;customer.Contact=contact;customer.UpdatedAt=DateTime.Now;
        data.AuditTrail.Add(new AuditEntry{Action="Cliente corrigido",Description=$"{original.Name} → {name}; nome e telefone atualizados. Valores financeiros preservados."});
    }
    // Cadastro manual (Clientes › Novo cliente). Mesma regra de duplicidade da edição: mesmo nome com o mesmo telefone é recusado.
    public static CustomerProfile Create(AppData data,Guid id,string name,string contact,string email,string cpf,string address,OperationContext context)
    {
        name=Clean(name);contact=PhoneText.Normalize(Clean(contact));
        if(name.Length==0||IsGeneric(name))throw new InvalidOperationException("Informe o nome do cliente.");
        if(data.Customers.Any(x=>x.Id==id))throw new InvalidOperationException("Este cliente já foi cadastrado. Atualize a lista.");
        var digits=PhoneText.Digits(contact);
        if(digits.Length>0&&data.Customers.Any(x=>x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)&&PhoneText.Digits(Clean(x.Contact))==digits))
            throw new InvalidOperationException("Já existe um cliente com esse nome e esse telefone.");
        if(!PhoneText.IsCompleteOrEmpty(contact))throw new InvalidOperationException(PhoneText.IncompleteMessage);
        if(PhoneOwner(data,contact,null) is {} owner)throw new InvalidOperationException(PhoneTaken(owner));
        var customer=new CustomerProfile{Id=id,Name=name,Contact=contact,Email=(email??"").Trim(),CpfCnpj=(cpf??"").Trim(),Address=(address??"").Trim(),UpdatedAt=DateTime.Now};
        data.Customers.Add(customer);
        AuditService.Record(data,context,"Cliente cadastrado","Cliente",id.ToString(),"",name,"",contact);
        return customer;
    }
    // Exclui só o cadastro. Pedidos, lançamentos e dívidas continuam com o nome/telefone escritos neles (histórico).
    public static void Delete(AppData data,Guid id,OperationContext context)
    {
        var customer=data.Customers.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Cliente não encontrado. Atualize a lista.");
        data.Customers.Remove(customer);
        AuditService.Record(data,context,"Cliente excluído","Cliente",id.ToString(),JsonSerializer.Serialize(customer),"","",$"{customer.Name} • {customer.Contact} — pedidos e lançamentos mantêm o nome registrado");
    }
    // Cria o cadastro de quem aparece em pedidos/lançamentos recentes. Regras:
    // - só registros dos últimos 2 dias (o histórico antigo não recria cadastros apagados ou corrigidos);
    // - cliente é nome + telefone: dois "Ricardo" com telefones diferentes são dois cadastros;
    // - só com telefone completo (DDD): lançamento de balcão sem número ou com número curto não vira cadastro;
    // - telefone que já pertence a alguém não gera outro cadastro; quem foi excluído não volta.
    public static void SyncFromRecords(AppData data,DateTime since)
    {
        var deleted=data.AuditTrail.Where(a=>a.Action=="Cliente excluído"&&a.OldValue.Length>0).Select(a=>{try{return JsonSerializer.Deserialize<CustomerProfile>(a.OldValue);}catch(JsonException){return null;}}).OfType<CustomerProfile>()
            .Select(c=>(Name:c.Name.Trim().ToLowerInvariant(),Digits:PhoneText.Digits(Clean(c.Contact)))).ToHashSet();
        var recent=data.ScheduledOrders.Where(x=>x.CreatedAt>=since).Select(x=>(x.CustomerName,x.Contact))
            .Concat(data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.CreatedAt>=since&&!x.Product.StartsWith("Ajuste do lançamento",StringComparison.OrdinalIgnoreCase)).Select(x=>(x.CustomerName,x.Contact)));
        foreach(var (rawName,rawContact) in recent)
        {
            var name=Clean(rawName);if(IsGeneric(name)||PhoneText.Digits(name).Length>=6)continue;
            var contact=PhoneText.Normalize(Clean(rawContact));var digits=PhoneText.Digits(contact);
            if(PhoneKey(contact).Length==0)continue;
            if(deleted.Contains((name.ToLowerInvariant(),digits)))continue;
            var sameName=data.Customers.Where(x=>x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)).ToList();
            if(sameName.Any(x=>PhoneText.Digits(Clean(x.Contact))==digits))continue;
            if(sameName.FirstOrDefault(x=>PhoneText.Digits(Clean(x.Contact)).Length==0) is CustomerProfile noPhone){noPhone.Contact=contact;noPhone.UpdatedAt=DateTime.Now;continue;}
            if(digits.Length>=8&&data.Customers.Any(x=>PhoneText.Digits(Clean(x.Contact)) is var d&&d.Length>=8&&(d.EndsWith(digits)||digits.EndsWith(d))))continue;
            data.Customers.Add(new CustomerProfile{Name=name,Contact=contact});
        }
    }
    static readonly string[] GenericNames=["não informado","nao informado","cliente balcão","cliente balcao","balcão","balcao",""];
    public static bool IsGeneric(string? name)=>GenericNames.Contains((name??"").Trim().ToLowerInvariant());
    // Cadastro antigo incompleto: sem nome de verdade ou sem telefone completo (com DDD).
    // Telefone como chave do cliente: só números com DDD, sem o 55 do país. Vazio = telefone incompleto (não conta como repetido).
    public static string PhoneKey(string? contact){var d=PhoneText.Digits(Clean(contact));if(d.Length>=12&&d.StartsWith("55"))d=d[2..];return d.Length is 10 or 11?d:"";}
    public static CustomerProfile? PhoneOwner(AppData data,string? contact,Guid? except){var key=PhoneKey(contact);return key.Length==0?null:data.Customers.FirstOrDefault(x=>x.Id!=except&&PhoneKey(x.Contact)==key);}
    public const string PhoneTakenPrefix="Este telefone já é do cliente ";
    public static string PhoneTaken(CustomerProfile owner)=>$"{PhoneTakenPrefix}{owner.Name}. Use o cadastro existente.";
    public static List<List<CustomerProfile>> PhoneDuplicates(AppData data)=>data.Customers.Where(x=>PhoneKey(x.Contact).Length>0).GroupBy(x=>PhoneKey(x.Contact)).Where(g=>g.Count()>1).Select(g=>g.OrderBy(x=>x.Name).ToList()).ToList();
    // Mesclar cadastros do mesmo telefone: fica um; ele recebe os dados que faltam (e-mail, CPF, endereço, tipo de preço, observações),
    // e os pedidos e dívidas dos outros passam para ele, com o nome dele. Lançamentos do caixa não mudam (são registro do caixa).
    // Os outros saem como "Cliente excluído" (não voltam pelo cadastro automático).
    public static void Merge(AppData data,Guid keepId,IReadOnlyCollection<Guid> removeIds,OperationContext context)
    {
        var keep=data.Customers.FirstOrDefault(x=>x.Id==keepId)??throw new InvalidOperationException("Cliente não encontrado. Atualize a lista.");
        var key=PhoneKey(keep.Contact);
        if(removeIds.Count==0||removeIds.Contains(keepId)||removeIds.Distinct().Count()!=removeIds.Count)throw new InvalidOperationException("Escolha os cadastros que serão unidos ao que fica.");
        var removed=removeIds.Select(id=>data.Customers.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Um dos cadastros já foi removido. Atualize a lista.")).ToList();
        if(key.Length==0||removed.Any(x=>PhoneKey(x.Contact)!=key))throw new InvalidOperationException("Só dá para mesclar cadastros com o mesmo telefone completo.");
        var before=JsonSerializer.Serialize(keep);int orders=0,debts=0;
        foreach(var r in removed)
        {
            if(keep.Email.Length==0)keep.Email=r.Email;if(keep.CpfCnpj.Length==0)keep.CpfCnpj=r.CpfCnpj;if(keep.Address.Length==0)keep.Address=r.Address;if(keep.PriceType.Length==0)keep.PriceType=r.PriceType;
            if(r.Notes.Trim().Length>0&&!keep.Notes.Contains(r.Notes.Trim()))keep.Notes=(keep.Notes.Trim()+"\n"+r.Notes.Trim()).Trim();
            foreach(var order in data.ScheduledOrders.Where(x=>ReceivableViews.Customer(data,x.CustomerId,x.CustomerName,x.Contact)?.Id==r.Id).ToList()){order.CustomerId=keep.Id;order.CustomerName=keep.Name;order.Revision++;orders++;}
            foreach(var debt in data.ManualDebts.Where(x=>ReceivableViews.Customer(data,x.CustomerId,x.CustomerName,x.Contact)?.Id==r.Id).ToList()){debt.CustomerId=keep.Id;debt.CustomerName=keep.Name;debts++;}
            // Lançamentos do caixa mantêm o nome escrito neles; o cadastro que fica lembra esse nome para continuar somando.
            var known=keep.AllNames().ToHashSet(StringComparer.CurrentCultureIgnoreCase);foreach(var name in r.AllNames().Where(x=>!IsGeneric(x)))if(known.Add(name))keep.MergedNames=(keep.MergedNames??"").Length==0?name:keep.MergedNames+"\n"+name;
        }
        keep.UpdatedAt=DateTime.Now;
        foreach(var r in removed)Delete(data,r.Id,context);
        AuditService.Record(data,context,"Clientes mesclados","Cliente",keep.Id.ToString(),before,JsonSerializer.Serialize(keep),"",$"{string.Join(", ",removed.Select(x=>x.Name))} → {keep.Name}; {orders} pedido(s) e {debts} dívida(s) passaram para {keep.Name}");
    }
    // Limpeza de 28/10/2026: sai quem não tem telefone completo com DDD; número completo sem nome fica.
    // ExpectedCount confere que a lista não mudou entre a contagem mostrada e a confirmação.
    public static bool HasFullPhone(CustomerProfile c)=>PhoneText.Digits(Clean(c.Contact)).Length is >=10 and <=13;
    public static int DeleteWithoutFullPhone(AppData data,int expectedCount,OperationContext context)
    {
        var list=data.Customers.Where(x=>!HasFullPhone(x)).ToList();
        if(list.Count!=expectedCount)throw new InvalidOperationException($"A lista mudou: agora são {list.Count} cadastro(s) sem telefone completo. Confira e tente de novo.");
        foreach(var c in list)Delete(data,c.Id,context);
        return list.Count;
    }
    public static bool IsIncomplete(CustomerProfile c)=>IsGeneric(Clean(c.Name))||!Clean(c.Name).Any(char.IsLetter)||PhoneText.Digits(c.Name).Length>=6||PhoneText.Digits(Clean(c.Contact)).Length is <10 or >13;
    public static string Clean(string? value)=>string.IsNullOrWhiteSpace(value)||new[]{"não informado","null","none"}.Contains(value.Trim().ToLowerInvariant())?"":value.Trim();
}
