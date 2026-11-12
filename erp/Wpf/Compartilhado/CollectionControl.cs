namespace GraficaCaixa;

public static class CollectionControl
{
    public static readonly string[] Statuses=["Não contatado","Cobrança enviada","Aguardando resposta","Prometeu pagar","Negociação","Sem retorno"];
    public static decimal Balance(AppData data,Guid id,bool order)
    {
        if(order){var item=data.ScheduledOrders.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Pedido não encontrado.");return item.PricePending?-1:item.RemainingAmount;}
        return (data.ManualDebts.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Dívida não encontrada.")).Remaining(data);
    }
    public static List<CollectionContact> History(AppData data,Guid id,bool order)=>data.CollectionContacts.Where(x=>x.DebtId==id&&x.IsOrder==order).OrderByDescending(x=>x.CreatedAt).ToList();
    public static string Status(AppData data,Guid id,bool order)=>Balance(data,id,order)==0?"Pago":History(data,id,order).FirstOrDefault()?.Status??(order?"Não contatado":data.ManualDebts.First(x=>x.Id==id).InitialCollectionStatus);
    public static void Register(AppData data,CollectionContact contact)
    {
        if(data.CollectionContacts.Any(x=>x.Id==contact.Id))return;
        if(Balance(data,contact.DebtId,contact.IsOrder)==0)throw new InvalidOperationException("Esta dívida já está paga. O histórico foi preservado.");
        if(!Statuses.Contains(contact.Status)||string.IsNullOrWhiteSpace(contact.Notes))throw new InvalidOperationException("Informe o resultado/status e uma observação do contato.");
        if(contact.CreatedAt>DateTime.Now.AddMinutes(1))throw new InvalidOperationException("A data do contato não pode ser futura.");
        contact.Notes=contact.Notes.Trim();data.CollectionContacts.Add(contact);
        data.AuditTrail.Add(new AuditEntry{Action="Contato de cobrança registrado",Description=$"{(contact.IsOrder?"Pedido":"Dívida")} {contact.DebtId} • {contact.Status}"});
    }
    public static string Message(AppData data,Guid id,bool order)
    {
        var balance=Balance(data,id,order);if(balance<=0)throw new InvalidOperationException(balance==0?"Não há saldo pendente para cobrar.":"Defina o valor do pedido antes de gerar a mensagem.");
        static string Clean(string? text)
        {
            var value=text?.Trim()??"";
            return new[]{"null","none","undefined","não informado"}.Contains(value,StringComparer.OrdinalIgnoreCase)?"":value;
        }
        string name,reference;
        if(order){var item=data.ScheduledOrders.First(x=>x.Id==id);name=Clean(item.CustomerName);reference=item.Number>0?$"ao pedido #{item.Number}":Clean(item.Description).Length>0?$"a {Clean(item.Description)}":"";}
        else{var item=data.ManualDebts.First(x=>x.Id==id);name=Clean(item.CustomerName);reference=Clean(item.Description).Length>0?$"a {Clean(item.Description)}":"";}
        // Texto em Ferramentas › Modelos de mensagem ("Cobrança de devedores"); a assinatura é o nome da gráfica em Configurações.
        bool Collection(CommunicationTemplate x)=>x.Name.Trim().Equals(CommunicationOperations.CollectionTemplateName,StringComparison.OrdinalIgnoreCase);
        var template=CommunicationOperations.EffectiveTemplates(data.Prompt12??new()).FirstOrDefault(Collection)??CommunicationOperations.DefaultTemplates().First(Collection);
        var amount=balance.ToString("C2",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var number=order?data.ScheduledOrders.First(x=>x.Id==id).Number:0;
        return MessageTemplates.Render(template.Body,new Dictionary<string,string>{["cliente"]=name,["saldo"]=amount,["referencia"]=reference.Length>0?$" referente {reference}":"",["pedido"]=number>0?$"#{number}":"",["grafica"]=data.Company?.Name??"",["pix"]=PixBrCode.MessageBlock(data.Company,balance,number>0?$"PED{number}":null)});
    }
}
