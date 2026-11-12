using System.Reflection;
using System.Text.Json;
namespace GraficaCaixa;

// Authorize actual persisted differences, never claimed actions or identity from the payload.
public static class ServerAuthorization
{
    public static string Json(object? value)=>JsonSerializer.Serialize(value);
    // A replay of an already committed 8D request is still valid after upgrading. Never ignore non-default new data.
    public static string? CompatibleLegacyCommitHash(CommitRequest request)
    {
        if(request.ClientOrderDetailsVersion!=8||request.Data.ProductionDeadlines!=null||(request.Data.Reminders??[]).Any(x=>x.ReadByUsers==null||x.ReadByUsers.Count>0)||request.Data.ScheduledOrders.SelectMany(x=>x.Payments).Any(x=>x.ExcessAmount!=0||x.ExcessConfirmed||!string.IsNullOrEmpty(x.ExcessKind)||x.OperationId!=null))return null;
        var root=System.Text.Json.Nodes.JsonNode.Parse(Json(request))!;var data=root["Data"]!;data.AsObject().Remove("ProductionDeadlines");foreach(var reminder in data["Reminders"]?.AsArray()??[])reminder?.AsObject().Remove("ReadByUsers");foreach(var order in data["ScheduledOrders"]?.AsArray()??[]){if(order==null)continue;foreach(var name in new[]{"AppliedAmount","CollectionSurcharge","CustomerExtra"})order.AsObject().Remove(name);foreach(var payment in order["Payments"]?.AsArray()??[])if(payment!=null)foreach(var name in new[]{"ExcessAmount","ExcessKind","ExcessConfirmed","OperationId","AppliedAmount"})payment.AsObject().Remove(name);}
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
    // Comparação de valores: 637 e 637,00 são o mesmo número (o texto do JSON não é). Só para decidir o que mudou; o hash do commit usa Json().
    static readonly JsonSerializerOptions Comparable=new(){Converters={new PlainDecimal()}};
    sealed class PlainDecimal:System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer,decimal value,JsonSerializerOptions options)=>writer.WriteNumberValue(value/1.0000000000000000000000000000m);
    }
    static bool Changed(object? a,object? b)=>JsonSerializer.Serialize(a,Comparable)!=JsonSerializer.Serialize(b,Comparable);
    // Primeiro item de cada Id (mesmo resultado de FirstOrDefault(x=>x.Id==id)), para buscas por Id sem varrer a lista.
    public static Dictionary<Guid,T> FirstById<T>(IEnumerable<T> items,Func<T,Guid> id){var map=new Dictionary<Guid,T>();foreach(var item in items)map.TryAdd(id(item),item);return map;}
    // Atalho: campos iguais (e mesmo tipo de data) dão o mesmo JSON. Quando algo difere, quem chama ainda compara pelo JSON, como antes.
    public static bool SameAudit(AuditEntry a,AuditEntry b)=>a.Id==b.Id&&a.Timestamp==b.Timestamp&&a.Timestamp.Kind==b.Timestamp.Kind&&a.SaleId==b.SaleId&&a.UserId==b.UserId&&a.UserLogin==b.UserLogin&&a.SessionId==b.SessionId&&a.Action==b.Action&&a.Description==b.Description&&a.Computer==b.Computer&&a.UserName==b.UserName&&a.EntityType==b.EntityType&&a.EntityId==b.EntityId&&a.OldValue==b.OldValue&&a.NewValue==b.NewValue&&a.Reason==b.Reason;
    public static AuditEntry CopyAudit(AuditEntry x)=>new(){Id=x.Id,Timestamp=x.Timestamp,SaleId=x.SaleId,UserId=x.UserId,UserLogin=x.UserLogin,SessionId=x.SessionId,Action=x.Action,Description=x.Description,Computer=x.Computer,UserName=x.UserName,EntityType=x.EntityType,EntityId=x.EntityId,OldValue=x.OldValue,NewValue=x.NewValue,Reason=x.Reason};
    // Cópia de trabalho para gravação: o mesmo que a cópia por JSON, mas a auditoria (metade dos dados) é copiada campo a campo.
    public static AppData CloneForWrite(AppData current)
    {
        // As versões de fechamento (maior bloco do banco, histórico que só cresce) também são copiadas campo a campo.
        var audit=current.AuditTrail;current.AuditTrail=[];var closures=current.CashClosureVersions;current.CashClosureVersions=[];
        try{var next=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(current))!;next.AuditTrail=audit.Select(CopyAudit).ToList();
            next.CashClosureVersions=closures.Select(x=>new CashClosureVersion{Id=x.Id,SessionId=x.SessionId,Version=x.Version,CreatedAt=x.CreatedAt,UserName=x.UserName,Reason=x.Reason,IsOriginal=x.IsOriginal,OpenedAt=x.OpenedAt,ClosedAt=x.ClosedAt,OpeningAmount=x.OpeningAmount,DeclaredClosingAmount=x.DeclaredClosingAmount,SalesTotal=x.SalesTotal,ExpectedCash=x.ExpectedCash,SnapshotJson=x.SnapshotJson}).ToList();return next;}
        finally{current.AuditTrail=audit;current.CashClosureVersions=closures;}
    }
    // Campos que o editor completa em pedido antigo sem que nada tenha mudado de fato (não contam como alteração).
    static bool SameAfterNormalization(ScheduledOrder old,ScheduledOrder order,string property)=>property switch
    {
        "OriginalTotal"=>!Changed(old.OriginalTotal??old.OrderTotal,order.OriginalTotal??order.OrderTotal),
        "DebtOriginDate"=>(old.DebtOriginDate??old.CreatedAt).Date==(order.DebtOriginDate??order.CreatedAt).Date,
        "ItemCostsEnabled"=>!old.ItemCostsEnabled&&old.Items.All(i=>i.UnitCost==null&&i.EnteredTotalCost==null&&!i.CostReviewed&&!i.CostIgnored),
        _=>false
    };
    // Lançamento em que só mudaram dados cadastrais (cliente, telefone, descrição, observação): não é correção financeira.
    public static bool OnlyInfoChanged(Sale before,Sale after)
    {
        var copy=JsonSerializer.Deserialize<Sale>(Json(after))!;
        copy.CustomerName=before.CustomerName;copy.Contact=before.Contact;copy.Product=before.Product;copy.Notes=before.Notes;
        if(copy.Items.Count!=before.Items.Count)return false;
        for(var i=0;i<copy.Items.Count;i++)copy.Items[i].Product=before.Items[i].Product;
        return Json(copy)==Json(before);
    }
    static bool OnlyCashLinkedDebtChanges(List<ManualDebt> before,List<ManualDebt> after)
    {
        if(before.Count!=after.Count||before.Any(b=>after.All(a=>a.Id!=b.Id)))return false;
        static string Core(ManualDebt d)=>Json(new{d.Id,d.Amount,d.Description,d.Notes,d.Items,d.OriginDate,d.CreatedAt,d.InitialCollectionStatus});
        return after.All(a=>{var b=before.First(x=>x.Id==a.Id);return Core(a)==Core(b)&&b.SaleIds.All(a.SaleIds.Contains)&&(b.CancelledAt==null||b.CancelledAt==a.CancelledAt);});
    }
    static readonly Dictionary<string,string> Views=new()
    {
        ["Products"]="view.products",["ProductCategories"]="view.products",["ScheduledOrders"]="view.orders",
        ["Customers"]="view.customers",["Sessions"]="view.cash",["LostEntries"]="view.cash",["FinancialCorrections"]="view.cash",["CashClosureVersions"]="view.closed",
        ["ManualDebts"]="view.debtors",["CollectionContacts"]="view.debtors",["Prospects"]="view.prospects",["BudgetRules"]="view.budget",["ProductionDeadlines"]="view.budget",["Reminders"]="view.reminders",["AuditTrail"]="system.audit"
    };
    static readonly Dictionary<string,string> Writes=new()
    {
        ["ProductCategories"]="products.edit",["Customers"]="customers.edit",["LostEntries"]="finance.correct",["ManualDebts"]="debtors.edit",
        ["CollectionContacts"]="debtors.edit",["Prospects"]="prospects.edit",["BudgetRules"]="budget.edit",["ProductionDeadlines"]="budget.edit",["Reminders"]="reminders.edit"
    };
    static readonly Dictionary<string,(string View,string Edit)> PrivateMaps=new()
    {
        ["PurchaseNeeds"]=("view.purchases","purchases.edit"),["Purchases"]=("view.purchases","purchases.edit"),["Suppliers"]=("view.purchases","purchases.edit"),
        ["Materials"]=("view.products","costs.edit"),["KnownYields"]=("view.products","costs.edit"),
        ["Losses"]=("system.owner","system.owner"),["Expenses"]=("system.owner","system.owner"),
        ["RetiredProfessionalQuotes"]=("system.owner","system.owner"),["FiscalDocuments"]=("system.owner","system.owner"),["Reworks"]=("system.owner","system.owner"),["Outsourced"]=("system.owner","system.owner"),
        ["CommunicationTemplates"]=("system.settings","system.settings"),["FiscalIssuerUrl"]=("system.settings","system.settings")
    };
    // Auditoria de listas (dívidas avulsas, clientes, cobranças…): guarda só os itens que entraram, saíram ou mudaram, não a lista
    // inteira (antes cada evento chegava a ~300 KB e a auditoria passou de 100 MB). Listas sem "Id" continuam inteiras.
    static (object? Before,object? After) OnlyChangedItems(object? before,object? after)
    {
        if(before is string||after is string||before is not System.Collections.IEnumerable||after is not System.Collections.IEnumerable)return (before,after);
        var b=JsonSerializer.SerializeToElement(before);var a=JsonSerializer.SerializeToElement(after);
        if(b.ValueKind!=JsonValueKind.Array||a.ValueKind!=JsonValueKind.Array)return (before,after);
        static string? Key(JsonElement e)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty("Id",out var id)?id.ToString():null;
        if(b.EnumerateArray().Any(x=>Key(x)==null)||a.EnumerateArray().Any(x=>Key(x)==null))return (before,after);
        var bm=b.EnumerateArray().GroupBy(x=>Key(x)!).ToDictionary(g=>g.Key,g=>g.First().GetRawText());var am=a.EnumerateArray().GroupBy(x=>Key(x)!).ToDictionary(g=>g.Key,g=>g.First().GetRawText());
        var removed=b.EnumerateArray().Where(x=>!am.TryGetValue(Key(x)!,out var y)||y!=x.GetRawText()).Select(x=>x.Clone()).ToList();
        var added=a.EnumerateArray().Where(x=>!bm.TryGetValue(Key(x)!,out var y)||y!=x.GetRawText()).Select(x=>x.Clone()).ToList();
        return (removed,added);
    }
    static object? Empty(PropertyInfo property)=>property.PropertyType==typeof(string)?"":Activator.CreateInstance(property.PropertyType);
    public static AppData View(AppData source,IReadOnlyDictionary<string,bool> permissions,bool owner=false)
    {
        var copy=JsonSerializer.Deserialize<AppData>(Json(source))!;
        foreach(var (name,key) in Views)if(!permissions.GetValueOrDefault(key)){var p=typeof(AppData).GetProperty(name)!;p.SetValue(copy,Empty(p));}
        // Cash and closed sessions share storage, but are independently visible.
        if(permissions.GetValueOrDefault("view.cash")||permissions.GetValueOrDefault("view.closed"))copy.Sessions=source.Sessions.Where(s=>s.IsOpen?permissions.GetValueOrDefault("view.cash"):permissions.GetValueOrDefault("view.closed")).Select(s=>JsonSerializer.Deserialize<CashSession>(Json(s))!).ToList();
        foreach(var (name,map) in PrivateMaps)if(!permissions.GetValueOrDefault(map.View)||map.View=="system.owner"&&!owner){var p=typeof(Prompt12State).GetProperty(name)!;p.SetValue(copy.Prompt12,Empty(p));}
        copy.Prompt12.LoginPassword=null;copy.Prompt12.OwnerPin=null;
        return copy;
    }
    public static void MergeHidden(AppData current,AppData incoming,IReadOnlyDictionary<string,bool> permissions,bool owner)
    {
        var visible=View(current,permissions,owner);
        foreach(var (name,key) in Views)
        {
            if(name=="Sessions")continue;
            if(name=="AuditTrail"&&!permissions.GetValueOrDefault(key)){if(incoming.AuditTrail.Any(a=>current.AuditTrail.Any(b=>b.Id==a.Id)))throw new UnauthorizedAccessException();incoming.AuditTrail=current.AuditTrail.Concat(incoming.AuditTrail).ToList();continue;}
            if(!permissions.GetValueOrDefault(key)){var p=typeof(AppData).GetProperty(name)!;if(Changed(p.GetValue(incoming),p.GetValue(visible)))throw new UnauthorizedAccessException();p.SetValue(incoming,p.GetValue(current));}
        }
        var visibleIds=visible.Sessions.Select(s=>s.Id).ToHashSet();
        foreach(var hidden in current.Sessions.Where(s=>!visibleIds.Contains(s.Id)))
        {if(incoming.Sessions.Any(s=>s.Id==hidden.Id))throw new UnauthorizedAccessException();incoming.Sessions.Add(hidden);}
        foreach(var (name,map) in PrivateMaps)if(!permissions.GetValueOrDefault(map.View)||map.View=="system.owner"&&!owner)
        {var p=typeof(Prompt12State).GetProperty(name)!;if(Changed(p.GetValue(incoming.Prompt12),p.GetValue(visible.Prompt12)))throw new UnauthorizedAccessException();p.SetValue(incoming.Prompt12,p.GetValue(current.Prompt12));}
        incoming.Prompt12.LoginPassword=current.Prompt12.LoginPassword;incoming.Prompt12.OwnerPin=current.Prompt12.OwnerPin;
    }
    static void Need(IReadOnlyDictionary<string,bool> p,string key){if(!p.GetValueOrDefault(key))throw new UnauthorizedAccessException();}
    static string Reason(IEnumerable<AuditEntry> claims,string id)=>claims.FirstOrDefault(x=>(x.EntityId==id||x.SaleId?.ToString()==id)&&!string.IsNullOrWhiteSpace(x.Reason))?.Reason.Trim()??"";
    static void Justify(string reason){if(reason.Length<5||reason.Length>1000)throw new InvalidOperationException("Informe uma justificativa de 5 a 1000 caracteres.");}
    public static void ValidateAndAudit(AppData current,AppData next,UserIdentity user,IReadOnlyDictionary<string,bool> p,Action<string>? requiredPermission=null,bool allowClosedValues=false)
    {
        void Need(IReadOnlyDictionary<string,bool> permissions,string key){ServerAuthorization.Need(permissions,key);requiredPermission?.Invoke(key);}
        // Desempenho: localiza cada registro por Id (antes varria a lista inteira para cada um: ~4.500² comparações). Mesma regra.
        var nextAudit=FirstById(next.AuditTrail,a=>a.Id);var currentAuditIds=current.AuditTrail.Select(a=>a.Id).ToHashSet();
        foreach(var entry in current.AuditTrail)if(!nextAudit.TryGetValue(entry.Id,out var existing)||!SameAudit(entry,existing)&&Changed(entry,existing))throw new InvalidOperationException("Auditoria histórica protegida.");
        var automaticProductCosts=new HashSet<Guid>();
        // Só os produtos (e os eventos que o recálculo de custo gera) são usados abaixo: não copia o banco inteiro.
        var expectedProducts=new AppData{Products=JsonSerializer.Deserialize<List<Product>>(Json(current.Products))!,AuditTrail=[]};
        foreach(var order in next.ScheduledOrders){var old=current.ScheduledOrders.FirstOrDefault(x=>x.Id==order.Id);if(order.ItemCostsEnabled&&!order.IgnoreCosts&&order.Items.Any(x=>x.CostReviewed&&!x.CostIgnored)&&(old==null||OrderOperations.CostSnapshot(old)!=OrderOperations.CostSnapshot(order))){Need(p,"costs.edit");OrderOperations.UpdateReviewedProductCosts(expectedProducts,old,order,new(user.Name,user.Role){Permissions=p});}}
        foreach(var product in expectedProducts.Products){var original=current.Products.First(x=>x.Id==product.Id);if(Changed(original,product)){automaticProductCosts.Add(product.Id);var incoming=next.Products.FirstOrDefault(x=>x.Id==product.Id);if(incoming!=null&&(!Changed(incoming,original)||Json(incoming)==Json(product)))next.Products[next.Products.IndexOf(incoming)]=product;}}
        var claims=next.AuditTrail.Where(a=>!currentAuditIds.Contains(a.Id)).ToList();
        next.AuditTrail=current.AuditTrail.ToList();
        void Event(string type,string id,string action,object? before,object? after,string reason="",string description="",Guid? saleId=null)
        {
            next.AuditTrail.Add(new AuditEntry{Timestamp=DateTime.UtcNow,UserId=user.UserId.ToString(),UserLogin=user.Login,SessionId=user.SessionId.ToString(),UserName=user.Name,Computer="Servidor / dispositivo "+user.DeviceId,EntityType=type,EntityId=id,Action=action,OldValue=before==null?"":Json(before),NewValue=after==null?"":Json(after),Reason=reason,Description=description,SaleId=saleId});
        }
        foreach(var prop in typeof(AppData).GetProperties().Where(x=>x.CanWrite))
        {
            if(prop.Name is "ScheduledOrders" or "Products" or "Sessions" or "FinancialCorrections" or "CashClosureVersions" or "AuditTrail" or "Prompt12" or "DataVersion" or "Reminders")continue;
            if(!Changed(prop.GetValue(current),prop.GetValue(next)))continue;
            if(prop.Name.EndsWith("Version"))throw new InvalidOperationException("Versão de dados protegida.");
            // Corrigir/cancelar o lançamento atualiza a conta gerada por ele; isso é operação de caixa, não edição de Devedores.
            if(prop.Name=="ManualDebts"&&OnlyCashLinkedDebtChanges(current.ManualDebts,next.ManualDebts)){Need(p,"view.cash");Event(prop.Name,"global","Conta do lançamento atualizada",null,null,Reason(claims,"global"));continue;}
            Need(p,Writes.GetValueOrDefault(prop.Name,"system.settings"));
            if(Views.TryGetValue(prop.Name,out var view))Need(p,view);
            var (changedBefore,changedAfter)=OnlyChangedItems(prop.GetValue(current),prop.GetValue(next));Event(prop.Name,"global","Cadastro / configuração alterado",changedBefore,changedAfter,Reason(claims,"global"));
        }
        if((current.ProductionDeadlines??[]).Any(x=>(next.ProductionDeadlines??[]).All(y=>y.Id!=x.Id)))throw new InvalidOperationException("Inative o prazo em vez de removê-lo.");
        foreach(var row in next.ProductionDeadlines??[]){var before=(current.ProductionDeadlines??[]).FirstOrDefault(x=>x.Id==row.Id);if(!Changed(before,row)&&(before!=null))continue;if(string.IsNullOrWhiteSpace(row.Description)||row.Description.Length>200||string.IsNullOrWhiteSpace(row.DeadlineText)||row.DeadlineText.Length>200||row.Note.Length>2000||row.Revision!=(before?.Revision??0)+1)throw new InvalidOperationException("Prazo inválido ou revisão incorreta.");}
        if((next.ProductionDeadlines??[]).Select(x=>x.Id).Distinct().Count()!=(next.ProductionDeadlines??[]).Count)throw new InvalidOperationException("Prazo duplicado.");
        foreach(var id in (current.Reminders??[]).Select(x=>x.Id).Union((next.Reminders??[]).Select(x=>x.Id)))
        {
            var before=(current.Reminders??[]).FirstOrDefault(x=>x.Id==id);var after=(next.Reminders??[]).FirstOrDefault(x=>x.Id==id);if(!Changed(before,after))continue;Need(p,"view.reminders");
            object? Content(Reminder? x)=>x==null?null:new{x.Id,x.Phone,x.Name,x.Title,x.Description,x.DueAt,x.CreatedAt,x.CreatedBy,x.DoneAt};
            if(Changed(Content(before),Content(after)))Need(p,"reminders.edit");
            if(after!=null&&after.ReadByUsers==null)throw new InvalidOperationException("Estado de leitura inválido.");
            foreach(var reader in after==null?Array.Empty<Guid>():(before?.ReadByUsers??[]).Keys.Union(after.ReadByUsers.Keys))if(reader!=user.UserId&&((before?.ReadByUsers??[]).GetValueOrDefault(reader)!=(after?.ReadByUsers??[]).GetValueOrDefault(reader)))throw new UnauthorizedAccessException("A leitura pertence a outro usuário.");
            if(after!=null&&after.ReadByUsers==null)throw new InvalidOperationException("Estado de leitura inválido.");
            if(before==null&&after?.ReadByUsers.Count>0)throw new InvalidOperationException("Novo lembrete deve começar não lido.");
            if(after!=null&&after.ReadByUsers.TryGetValue(user.UserId,out var readAt)&&(before==null||before.ReadByUsers.GetValueOrDefault(user.UserId)!=readAt))after.ReadByUsers[user.UserId]=DateTime.UtcNow;
            Event("Lembrete",id.ToString(),Changed(Content(before),Content(after))?"Lembrete alterado":"Leitura do lembrete alterada",before,after);
        }
        foreach(var prop in typeof(Prompt12State).GetProperties().Where(x=>x.CanWrite))
        {
            if(prop.Name is "LoginPassword" or "OwnerPin")continue;
            if(!Changed(prop.GetValue(current.Prompt12),prop.GetValue(next.Prompt12)))continue;
            if(prop.Name=="Version")throw new InvalidOperationException();
            var map=PrivateMaps.GetValueOrDefault(prop.Name,(View:"system.settings",Edit:"system.settings"));Need(p,map.Edit);Need(p,map.View);
            Event(prop.Name,"global","Registro alterado",prop.GetValue(current.Prompt12),prop.GetValue(next.Prompt12),Reason(claims,"global"));
        }
        foreach(var id in current.Products.Select(x=>x.Id).Union(next.Products.Select(x=>x.Id)))
        {
            var old=current.Products.FirstOrDefault(x=>x.Id==id);var value=next.Products.FirstOrDefault(x=>x.Id==id);if(!Changed(old,value))continue;
            if(automaticProductCosts.Contains(id)&&Json(value)==Json(expectedProducts.Products.First(x=>x.Id==id))){Need(p,"view.orders");Need(p,"costs.edit");}else{Need(p,"view.products");Need(p,"products.edit");}
            var a=JsonSerializer.SerializeToElement(old);var b=JsonSerializer.SerializeToElement(value);
            foreach(var field in typeof(Product).GetProperties().Where(x=>x.CanWrite&&x.Name.Contains("Cost",StringComparison.OrdinalIgnoreCase)))if(Changed(field.GetValue(old??new Product()),field.GetValue(value??new Product())))Need(p,"costs.edit");
            if(Changed(old?.ExactQuantityPrices.Where(x=>x.TotalCost.HasValue).Select(x=>new{x.Quantity,x.TotalCost}).ToList()??[],value?.ExactQuantityPrices.Where(x=>x.TotalCost.HasValue).Select(x=>new{x.Quantity,x.TotalCost}).ToList()??[])||Changed(old?.QuantityPriceTiers.Where(x=>x.UnitCost.HasValue).Select(x=>new{x.From,x.Through,x.UnitCost}).ToList()??[],value?.QuantityPriceTiers.Where(x=>x.UnitCost.HasValue).Select(x=>new{x.From,x.Through,x.UnitCost}).ToList()??[]))Need(p,"costs.edit");
            Event("Produto",id.ToString(),automaticProductCosts.Contains(id)?"Custo confirmado pelo pedido":old==null?"Produto criado":value==null?"Produto removido":"Produto alterado",old,value,description:automaticProductCosts.Contains(id)?string.Join("; ",expectedProducts.AuditTrail.Where(x=>x.EntityType=="Produto"&&x.EntityId==id.ToString()&&!currentAuditIds.Contains(x.Id)).Select(x=>x.Description)):"");
        }
        foreach(var old in current.ScheduledOrders)if(next.ScheduledOrders.All(x=>x.Id!=old.Id))throw new InvalidOperationException("Pedidos com histórico não podem ser excluídos. Cancele ou arquive.");
        // Tipo de preço do cliente muda o preço dos próximos lançamentos: além de alterar clientes, exige alterar preços (qualquer caminho de gravação).
        foreach(var customer in next.Customers){var before=current.Customers.FirstOrDefault(x=>x.Id==customer.Id)?.PriceType??"";if((customer.PriceType??"")!=before){Need(p,"customers.edit");Need(p,"products.edit");}}
        foreach(var order in next.ScheduledOrders)
        {
            var old=current.ScheduledOrders.FirstOrDefault(x=>x.Id==order.Id);if(!Changed(old,order))continue;Need(p,"view.orders");
            var reason=Reason(claims,order.Id.ToString());
            if(string.IsNullOrWhiteSpace(reason))reason=order.CostChangeReason;
            // Pedido pago: justificativa só quando o total fica abaixo do que já foi recebido (ou volta a "a definir"). Incluir item ou aumentar valor não pede.
            var belowPaid=old!=null&&old.AppliedAmount>0&&(order.PricePending&&!old.PricePending||!order.PricePending&&order.OrderTotal<old.AppliedAmount);
            if(old==null){Need(p,"orders.create");if(order.Stage=="Entregue")Need(p,"orders.deliver");if(order.Items.Any(i=>i.UnitCost>0||i.EnteredTotalCost>0)||order.FreightCost>0||order.AdditionalCost>0)Need(p,"costs.edit");}
            else
            {
                if(order.Number!=old.Number||order.CreatedAt!=old.CreatedAt)throw new InvalidOperationException("Identidade do pedido protegida.");
                foreach(var prop in typeof(ScheduledOrder).GetProperties().Where(x=>x.CanWrite))
                {
                    if(!Changed(prop.GetValue(old),prop.GetValue(order))||SameAfterNormalization(old,order,prop.Name))continue;
                    var key=prop.Name switch
                    {
                        "Id" or "Revision" or "PdfGeneratedAt" or "CostChangeReason" or "ReadyAt"=>"", // ReadyAt: preenchido pelo servidor ao mudar a etapa.
                        "Stage"=>order.Stage=="Entregue"?"orders.deliver":"orders.stage",
                        "PayAfterDelivery" when order.Stage=="Entregue"&&old.Stage!="Entregue"=>"orders.deliver",
                        "PayAfterDelivery" when !order.PayAfterDelivery&&order.FullyPaid&&order.Payments.Any(x=>old.Payments.All(y=>y.Id!=x.Id)&&x.Amount>0)=>"",
                        "IsUncollected" or "ArchivedAsUncollectedAt" or "CancelledAt" or "CancelReason" or "CancelledBy"=>"orders.cancel",
                        "Items"=>"", // Granular comparison below.
                        "Payments" or "DepositSaleId" or "FinalSaleId" or "FullyPaid" or "PaymentReceivedAt" or "DepositPaid" or "DepositAmount" or "DepositPaymentType" or "FinalPaymentType"=>"",
                        "OrderTotal" or "OriginalTotal" or "DiscountPercent" or "FixedDiscount" or "PricePending" or "UseItemPrices"=>"orders.value",
                        "ItemCostsEnabled" or "IgnoreCosts" or "FreightCost" or "AdditionalCost" or "CostNotes"=>"costs.edit",
                        "ArtworkApprovedAt" or "SentToProductionAt" or "DueDate"=>"orders.stage",
                        _=>"orders.edit"
                    };
                    if(key!="")Need(p,key);
                    // Nome, telefone e observações são cadastrais; "pagar na retirada" e a data prevista só dizem quando o saldo será pago: mudam sem justificativa mesmo com pagamento.
                    if(belowPaid&&key is "orders.value" or "orders.edit" or "orders.items"&&prop.Name is not ("CustomerName" or "Contact" or "CustomerId" or "Notes" or "PayAfterDelivery" or "ExpectedPaymentDate")){Justify(reason);}
                }
                foreach(var id in old.Items.Select(x=>x.Id).Union(order.Items.Select(x=>x.Id)))
                {
                    var before=old.Items.FirstOrDefault(x=>x.Id==id);var after=order.Items.FirstOrDefault(x=>x.Id==id);if(!Changed(before,after))continue;var itemValueChanged=false;
                    if(before==null||after==null){Need(p,"orders.items");if(after?.UnitCost!=null||after?.EnteredTotalCost!=null)Need(p,"costs.edit");}
                    else foreach(var prop in typeof(OrderItem).GetProperties().Where(x=>x.CanWrite))if(Changed(prop.GetValue(before),prop.GetValue(after))){Need(p,prop.Name=="Quantity"?"orders.quantity":prop.Name=="UnitPrice"?"orders.value":prop.Name.Contains("Cost")?"costs.edit":"orders.items");if(!prop.Name.Contains("Cost"))itemValueChanged=true;}
                    // Só custo mudou (Custo, Conferido, Ignorar): controle interno, sem justificativa.
                    if(belowPaid&&(before==null||after==null||itemValueChanged)){Justify(reason);}
                }
                foreach(var payment in old.Payments){var after=order.Payments.FirstOrDefault(x=>x.Id==payment.Id)??throw new InvalidOperationException("Pagamento não pode ser apagado.");if(payment.Amount!=after.Amount||payment.SaleId!=after.SaleId||payment.PaidAt!=after.PaidAt||payment.ExcessAmount!=after.ExcessAmount||payment.ExcessKind!=after.ExcessKind||payment.ExcessConfirmed!=after.ExcessConfirmed||payment.OperationId!=after.OperationId)throw new InvalidOperationException("Pagamento original protegido. Use correção financeira.");if(Changed(payment,after)){Need(p,"finance.method");Justify(reason.Length>0?reason:Reason(claims,payment.SaleId?.ToString()??""));}}
            }
            foreach(var payment in order.Payments.Where(x=>old==null||old.Payments.All(y=>y.Id!=x.Id)))
            {
                Need(p,"finance.receive");if(!PaymentPolicy.DefaultRates.ContainsKey(payment.PaymentType)||payment.Amount==0||decimal.Round(payment.Amount,2)!=payment.Amount||payment.SaleId==null)throw new InvalidOperationException("Pagamento inválido.");
                if(payment.Amount<0&&!next.FinancialCorrections.Any(c=>c.AdjustmentSaleId==payment.SaleId))throw new InvalidOperationException("Estorno exige correção vinculada.");
                var sale=next.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==payment.SaleId);if(sale==null||sale.Total!=payment.Amount||sale.PaymentType!=payment.PaymentType)throw new InvalidOperationException("Pagamento sem lançamento correspondente.");
                if(payment.Amount>0){var priorApplied=(old?.AppliedAmount??0)+order.Payments.TakeWhile(x=>x.Id!=payment.Id).Where(x=>old==null||old.Payments.All(y=>y.Id!=x.Id)).Sum(x=>x.AppliedAmount);var expectedExcess=Math.Max(0,payment.Amount-Math.Max(0,order.OrderTotal-priorApplied));if(payment.ExcessAmount!=expectedExcess||payment.ExcessAmount>0&&(!payment.ExcessConfirmed||payment.ExcessKind!=PaymentPolicy.CollectionSurcharge&&payment.ExcessKind!=PaymentPolicy.CustomerExtra)||payment.FeePercent!=PaymentPolicy.Rate(payment.PaymentType)||sale.FeePercent!=payment.FeePercent)throw new InvalidOperationException("Composição financeira ou confirmação do excedente inválida.");}
                if(payment.Amount<0){var correction=next.FinancialCorrections.Single(x=>x.AdjustmentSaleId==payment.SaleId);var original=order.Payments.FirstOrDefault(x=>x.SaleId==correction.OriginalSaleId)??throw new InvalidOperationException("Pagamento original da correção ausente.");var priorExtra=order.Payments.TakeWhile(x=>x.Id!=payment.Id).Where(x=>x.SaleId==correction.OriginalSaleId||next.FinancialCorrections.Any(c=>c.OriginalSaleId==correction.OriginalSaleId&&c.AdjustmentSaleId==x.SaleId)).Sum(x=>x.ExcessAmount);var expectedExtra=-Math.Min(priorExtra,-payment.Amount);if(payment.ExcessAmount!=expectedExtra||expectedExtra!=0&&(payment.ExcessKind!=original.ExcessKind||!payment.ExcessConfirmed))throw new InvalidOperationException("Composição do estorno inválida.");}
                Event("Pedido",order.Id.ToString(),payment.Amount<0?"Pagamento estornado":"Pagamento recebido",null,payment,reason,$"Pedido #{order.Number}: {payment.Amount:C2} via {payment.PaymentType}",payment.SaleId);
            }
            if(!OrderStages.All.Contains(order.Stage)||order.OrderTotal<0||order.Items.Any(x=>x.Quantity<=0)||order.PricePending&&order.PaidAmount>0)throw new InvalidOperationException("Estado do pedido inválido.");
            if(order.Stage=="Entregue"&&!order.FullyPaid&&!order.PayAfterDelivery)throw new InvalidOperationException("Mantenha o saldo a receber ao entregar.");
            if(order.FullyPaid!=(!order.PricePending&&order.RemainingAmount<=0))throw new InvalidOperationException("Quitação deve corresponder aos pagamentos.");
            Event("Pedido",order.Id.ToString(),old==null?"Pedido criado":old.IsUncollected!=order.IsUncollected?order.IsUncollected?"Pedido cancelado / arquivado":"Pedido restaurado":old.Stage!=order.Stage?order.Stage=="Entregue"?"Pedido entregue":"Etapa alterada":"Pedido alterado",old,order,reason,$"Pedido #{order.Number}");
        }
        foreach(var old in current.Sessions)if(next.Sessions.All(x=>x.Id!=old.Id))throw new InvalidOperationException("Caixa não pode ser excluído.");
        foreach(var session in next.Sessions)
        {
            var old=current.Sessions.FirstOrDefault(x=>x.Id==session.Id);if(!Changed(old,session))continue;Need(p,old?.IsOpen==false?"view.closed":"view.cash");
            if(old==null){Need(p,"cash.open");if(session.ClosedAt!=null||session.OpeningAmount<0||decimal.Round(session.OpeningAmount,2)!=session.OpeningAmount)throw new InvalidOperationException();session.OpenedBy=user.Name;Event("Caixa",session.Id.ToString(),"Caixa aberto",null,new{session.OpenedAt,session.OpeningAmount});}
            else
            {
                if(!(allowClosedValues&&!old.IsOpen)&&(session.OpenedAt!=old.OpenedAt||session.OpeningAmount!=old.OpeningAmount||session.OpenedBy!=old.OpenedBy))throw new InvalidOperationException("Abertura protegida.");
                if(!(allowClosedValues&&!old.IsOpen)&&session.ClosedAt==old.ClosedAt&&session.DeclaredClosingAmount==old.DeclaredClosingAmount&&session.ClosedBy!=old.ClosedBy)throw new InvalidOperationException("Autoria do fechamento protegida.");
                if(!old.IsOpen){Need(p,"cash.rectify");var reason=Reason(claims,session.Id.ToString());if(string.IsNullOrWhiteSpace(reason))reason=claims.FirstOrDefault(a=>a.SaleId.HasValue&&session.Sales.Any(s=>s.Id==a.SaleId.Value)&&!string.IsNullOrWhiteSpace(a.Reason))?.Reason??"";Justify(reason);if(!current.Sessions.Where(s=>!s.IsOpen).OrderByDescending(s=>s.ClosedAt).Take(5).Any(s=>s.Id==session.Id))throw new InvalidOperationException("Somente os cinco fechamentos mais recentes podem ser retificados.");if(session.IsOpen)throw new InvalidOperationException("Reabertura não suportada.");}
                if(old.ClosedAt!=session.ClosedAt||old.DeclaredClosingAmount!=session.DeclaredClosingAmount){Need(p,old.IsOpen?"cash.close":"cash.rectify");if(session.ClosedAt==null||session.DeclaredClosingAmount<0)throw new InvalidOperationException();session.ClosedBy=user.Name;Event("Caixa",session.Id.ToString(),"Caixa fechado / retificado",new{old.ClosedAt,old.DeclaredClosingAmount},new{session.ClosedAt,session.DeclaredClosingAmount});}
                foreach(var w in old.Withdrawals)if(!session.Withdrawals.Any(n=>n.Id==w.Id&&Json(n)==Json(w)))throw new InvalidOperationException("Sangria histórica protegida.");
                foreach(var w in old.Adjustments)if(!session.Adjustments.Any(n=>n.Id==w.Id&&Json(n)==Json(w)))throw new InvalidOperationException("Ajuste de caixa histórico protegido.");
                foreach(var sale in old.Sales)if(!session.Sales.Any(n=>n.Id==sale.Id))throw new InvalidOperationException("Lançamento histórico protegido.");
            }
            foreach(var w in session.Withdrawals.Where(w=>old==null||old.Withdrawals.All(n=>n.Id!=w.Id)))
            {Need(p,"cash.withdraw");Justify(w.Reason);if(old?.IsOpen!=true||w.Amount<=0||decimal.Round(w.Amount,2)!=w.Amount||w.Amount>(old.ExpectedCash-session.Withdrawals.Where(n=>old.Withdrawals.All(o=>o.Id!=n.Id)&&n.Id!=w.Id).Sum(n=>n.Amount))||w.CreatedAt<session.OpenedAt||w.CreatedAt>DateTime.Now.AddMinutes(1))throw new InvalidOperationException("Sangria inválida ou sem saldo.");w.UserName=user.Name;Event("Sangria",w.Id.ToString(),"Sangria realizada",null,w,w.Reason,$"Caixa {session.Id}");}
            foreach(var w in session.Adjustments.Where(w=>old==null||old.Adjustments.All(n=>n.Id!=w.Id)))
            {Need(p,"cash.adjust");Justify(w.Reason);if(old?.IsOpen!=true||w.Amount==0||decimal.Round(w.Amount,2)!=w.Amount||w.Amount<0&&session.ExpectedCash<0||w.CreatedAt<session.OpenedAt||w.CreatedAt>DateTime.Now.AddMinutes(1))throw new InvalidOperationException("Ajuste de caixa inválido.");w.UserName=user.Name;Event("Ajuste de caixa",w.Id.ToString(),"Ajuste de caixa",null,w,w.Reason,$"Caixa {session.Id}");}
            foreach(var sale in session.Sales)
            {
                var before=old?.Sales.FirstOrDefault(x=>x.Id==sale.Id);if(!Changed(before,sale))continue;
                if(!PaymentPolicy.DefaultRates.ContainsKey(sale.PaymentType)||sale.FeePercent is <0 or >100||decimal.Round(sale.Total,2)!=sale.Total||sale.Total==0)throw new InvalidOperationException("Lançamento financeiro inválido.");
                var reason=Reason(claims,sale.Id.ToString());
                if(before==null){Need(p,sale.Total<0?"finance.reverse":"finance.receive");if(old?.IsOpen==false){Need(p,"cash.rectify");Justify(reason);}if(sale.Total<0&&!next.FinancialCorrections.Any(c=>c.AdjustmentSaleId==sale.Id))throw new InvalidOperationException("Estorno sem vínculo.");}
                else if(OnlyInfoChanged(before,sale)){}
                else {Need(p,"finance.correct");if(sale.PaymentType!=before.PaymentType)Need(p,"finance.method");Justify(reason);if(sale.Total!=before.Total||sale.CreatedAt!=before.CreatedAt||sale.PaymentGroupId!=before.PaymentGroupId)throw new InvalidOperationException("Valor e identidade originais protegidos.");}
                Event("Lançamento",sale.Id.ToString(),before==null?"Lançamento registrado":"Lançamento corrigido",before,sale,reason,sale.Product,sale.Id);
            }
        }
        foreach(var correction in next.FinancialCorrections.Where(x=>current.FinancialCorrections.All(y=>y.Id!=x.Id)))
        {Need(p,"finance.correct");if(correction.CorrectedAmount==0)Need(p,"finance.reverse");Justify(correction.Reason);var original=next.Sessions.SelectMany(s=>s.Sales).FirstOrDefault(s=>s.Id==correction.OriginalSaleId);var adjustment=next.Sessions.SelectMany(s=>s.Sales).FirstOrDefault(s=>s.Id==correction.AdjustmentSaleId);var effective=current.FinancialCorrections.Where(c=>c.OriginalSaleId==correction.OriginalSaleId).OrderBy(c=>c.CreatedAt).LastOrDefault()?.CorrectedAmount??original?.Total;if(original==null||adjustment==null||correction.CorrectedAmount<0||adjustment.Total!=correction.CorrectedAmount-effective)throw new InvalidOperationException("Correção financeira inconsistente.");correction.UserName=user.Name;Event("Correção",correction.Id.ToString(),"Correção financeira",null,correction,correction.Reason,"",correction.OriginalSaleId);}
        foreach(var version in next.CashClosureVersions.Where(x=>current.CashClosureVersions.All(y=>y.Id!=x.Id)))
        {Need(p,version.IsOriginal?"cash.close":"cash.rectify");var session=next.Sessions.FirstOrDefault(s=>s.Id==version.SessionId)??throw new InvalidOperationException();if(!version.IsOriginal)Justify(version.Reason);version.UserName=user.Name;version.CreatedAt=DateTime.UtcNow;version.SnapshotJson=Json(session);version.OpenedAt=session.OpenedAt;version.ClosedAt=session.ClosedAt;version.OpeningAmount=session.OpeningAmount;version.DeclaredClosingAmount=session.DeclaredClosingAmount;version.SalesTotal=session.SalesTotal;version.ExpectedCash=session.ExpectedCash;}
        if(next.Sessions.Count(s=>s.IsOpen)>1)throw new InvalidOperationException("Há um caixa aberto.");
        var usedPayments=next.ScheduledOrders.SelectMany(o=>o.Payments).Where(x=>x.SaleId!=null).Select(x=>x.SaleId).ToList();if(usedPayments.Count!=usedPayments.Distinct().Count())throw new InvalidOperationException("Lançamento vinculado a mais de um pagamento.");
    }
}
