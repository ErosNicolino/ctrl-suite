using System.Text.Json;
using System.Text.Json.Serialization;
namespace GraficaCaixa;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopOrderCommand(Guid OperationId,long? ExpectedRevision,ScheduledOrder Order,Guid? CorrectedSale=null,decimal? CorrectedAmount=null,string CorrectionReason="");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopPaymentCommand(Guid OperationId,long ExpectedRevision,List<PaymentPart> Payments,DateTime PaidAt,List<Guid>? LinkSales=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopCustomerCommand(Guid OperationId,string ExpectedRevision,string Name,string Contact,string Email,string CpfCnpj,string Address);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopNotesCommand(Guid OperationId,string ExpectedRevision,string Text);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopProductCommand(Guid OperationId,string? ExpectedRevision,Product Product,string? ExpectedDocumentsRoot=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopPhotoUpload(string Description,byte[] Jpeg);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopPhotoDiscard(List<string> Paths);
public sealed record DesktopPhotoSaved(string Path);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopReminderCommand(Guid OperationId,Guid Id,string? ExpectedRevision,Reminder? Reminder);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesktopNeedCommand(Guid OperationId,string? ExpectedRevision,PurchaseNeed Need);
public sealed record DesktopNotesState(string Text,string Revision);
public sealed record DesktopWriteResult(Guid OperationId,long Revision,string EntityRevision,Guid[] ChangedSessions,bool Replayed=false,JsonElement? Value=null,string[]? RequiredPermissions=null);
public sealed partial class DataStore
{
    public static string DesktopRevision(object? value)=>CustomerRevision.Hash(JsonSerializer.Serialize(value));
    static void DesktopExpected(object? current,string? expected){if(current==null?expected!=null:expected!=DesktopRevision(current))throw new WebRequestException(409,"Registro alterado em outro computador. Atualize antes de salvar.");}
    public DesktopWriteResult DesktopSaveOrder(UserIdentity actor,DesktopOrderCommand command)=>DesktopExecute(actor,command.OperationId,"order",command,(next,p)=>{
        var old=next.ScheduledOrders.FirstOrDefault(x=>x.Id==command.Order.Id);
        if(old==null?command.ExpectedRevision!=null:command.ExpectedRevision!=old.Revision)throw new WebRequestException(409,"Pedido alterado. Atualize antes de salvar.");
        ReceivableViews.Link(next,command.Order);var changed=OrderOperations.Save(next,command.Order,new(actor.Name,actor.Role){Permissions=p},command.CorrectedSale,command.CorrectedAmount,command.CorrectionReason);
        return (DesktopRevision(next.ScheduledOrders.Single(x=>x.Id==command.Order.Id)),changed.ToArray());
    });
    public DesktopWriteResult DesktopReceive(UserIdentity actor,Guid id,DesktopPaymentCommand command)=>DesktopExecute(actor,command.OperationId,"payment",new{id,command},(next,p)=>{
        MobileAccess.Require(p,"view.orders","view.cash","finance.receive");var order=MobileFind(next,id,command.ExpectedRevision);if(ReceivableViews.ActiveLoss(next,id)!=null)throw new WebRequestException(409,"Este pedido está em perda. Reabra a cobrança em Devedores antes de receber.");
        var ctx=new OperationContext(actor.Name,actor.Role){Permissions=p};if(command.LinkSales is {Count:>0})OrderOperations.LinkSales(next,order,command.LinkSales,ctx);if(command.Payments.Count>0)OrderOperations.AddPayments(next,order,command.Payments,command.PaidAt,ctx);return(DesktopRevision(order),Array.Empty<Guid>());
    });
    public DesktopWriteResult DesktopSaveCustomer(UserIdentity actor,Guid id,DesktopCustomerCommand command)=>DesktopExecute(actor,command.OperationId,"customer",new{id,command},(next,p)=>{
        MobileAccess.Require(p,"view.customers","customers.edit");var customer=next.Customers.FirstOrDefault(x=>x.Id==id);
        // Novo cliente (sem revisão esperada): cria o cadastro.
        if(customer==null&&command.ExpectedRevision==null){try{customer=CustomerBasicOperations.Create(next,id,command.Name,command.Contact,command.Email,command.CpfCnpj,command.Address,new(actor.Name,actor.Role){Permissions=p});}catch(InvalidOperationException ex){throw new WebRequestException(422,ex.Message);}return(DesktopRevision(customer),Array.Empty<Guid>());}
        if(customer==null)throw new WebRequestException(404,"Cliente não encontrado.");
        DesktopExpected(customer,command.ExpectedRevision);
        CustomerBasicOperations.Edit(next,JsonSerializer.Deserialize<CustomerProfile>(JsonSerializer.Serialize(customer))!,command.Name,command.Contact,new(actor.Name,actor.Role){Permissions=p});
        customer.Email=command.Email;customer.CpfCnpj=command.CpfCnpj;customer.Address=command.Address;
        AuditService.Record(next,new(actor.Name,actor.Role){Permissions=p},"Dados fiscais do cliente atualizados","Cliente",customer.Id.ToString(),"","dados opcionais atualizados","","");
        return(DesktopRevision(customer),Array.Empty<Guid>());
    });
    public DesktopWriteResult DesktopSaveNotes(UserIdentity actor,DesktopNotesCommand command)=>DesktopExecute(actor,command.OperationId,"notes",command,(next,p)=>{
        MobileAccess.Require(p,"system.settings");DesktopExpected(next.ImprovementNotes,command.ExpectedRevision);next.ImprovementNotes=command.Text;return(DesktopRevision(command.Text),Array.Empty<Guid>());
    });
    // Fotos trocadas ou removidas saem da pasta depois de gravar (só se nenhum produto as usa). Sem pasta configurada (testes): nada é apagado.
    public DesktopWriteResult DesktopSaveProduct(UserIdentity actor,DesktopProductCommand command,string photosDirectory=""){List<string> removed=[];var result=DesktopExecute(actor,command.OperationId,"product",command,(next,p)=>{
        MobileAccess.Require(p,"view.products","products.edit");if(command.ExpectedDocumentsRoot!=null&&command.ExpectedDocumentsRoot!=next.DocumentsRoot)throw new WebRequestException(409,"A pasta dos arquivos mudou.");var old=next.Products.FirstOrDefault(x=>x.Id==command.Product.Id);var visible=old==null?null:JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(old))!;
        if(visible!=null&&!p.GetValueOrDefault("costs.edit")){visible.Cost=null;foreach(var x in visible.ExactQuantityPrices)x.TotalCost=null;foreach(var x in visible.QuantityPriceTiers)x.UnitCost=null;}
        DesktopExpected(visible,command.ExpectedRevision);
        var draft=JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(command.Product))!;
        if(old!=null&&!p.GetValueOrDefault("costs.edit")){
            if(command.Product.Cost!=null||command.Product.ExactQuantityPrices.Any(x=>x.TotalCost!=null)||command.Product.QuantityPriceTiers.Any(x=>x.UnitCost!=null))throw new UnauthorizedAccessException();
            draft.Cost=old.Cost;
        }
        if(old!=null)removed=ProductPhotoNames.All(old).Except(ProductPhotoNames.All(draft),StringComparer.OrdinalIgnoreCase).ToList();
        ProductOperations.Save(next,draft,old,new(actor.Name,actor.Role){Permissions=p});return(DesktopRevision(next.Products.Single(x=>x.Id==command.Product.Id)),Array.Empty<Guid>());
    });
        if(removed.Count>0)ReadWork(db=>WebFiles.DeleteUnusedPhotos(photosDirectory,removed,ReadShared(db).Products));return result;}
    public DesktopPhotoSaved DesktopUploadPhoto(UserIdentity actor,DesktopPhotoUpload command,string photosDirectory)
    {ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.products","products.edit");return 0;});return new(WebFiles.SaveProductPhoto(photosDirectory,command.Description,command.Jpeg));}
    public int DesktopDiscardPhotos(UserIdentity actor,DesktopPhotoDiscard command,string photosDirectory)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.products","products.edit");if(command.Paths.Count>ProductPhotoNames.Max*2)throw new WebRequestException(400,"Fotos demais.");return WebFiles.DeleteUnusedPhotos(photosDirectory,command.Paths,ReadShared(db).Products);});
    public string? DesktopPhotoFile(UserIdentity actor,Guid id,int index,WebApiOptions options)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.products");return WebFiles.Image(db,id,options,index);});
    public DesktopWriteResult DesktopSaveReminder(UserIdentity actor,DesktopReminderCommand command)=>DesktopExecute(actor,command.OperationId,"reminder",command,(next,p)=>{
        MobileAccess.Require(p,"view.reminders");next.Reminders??=[];var old=next.Reminders.FirstOrDefault(x=>x.Id==command.Id);DesktopExpected(old,command.ExpectedRevision);
        if(command.Reminder!=null&&command.Reminder.Id!=command.Id)throw new WebRequestException(400,"Identidade inválida.");
        if(command.Reminder is {} rem&&(rem.Title.Length>200||rem.Description.Length>2000||rem.Title.Trim().Length==0&&rem.Description.Trim().Length==0))throw new WebRequestException(422,"Informe o título ou o que verificar (título até 200 caracteres).");
        if(command.Reminder is {} named){named.Name=(named.Name??"").Trim();if(named.Name.Length>180)throw new WebRequestException(422,"O nome pode ter até 180 caracteres.");
            // Sem nome digitado: usa o nome do cliente cadastrado com o mesmo telefone (como no computador sem servidor).
            var digits=PhoneText.Digits(named.Phone);if(named.Name.Length==0&&digits.Length>=8)named.Name=next.Customers.FirstOrDefault(c=>{var d=PhoneText.Digits(c.Contact);return d.Length>=8&&(d.EndsWith(digits)||digits.EndsWith(d));})?.Name??"";}
        if(old!=null)next.Reminders.Remove(old);if(command.Reminder!=null)next.Reminders.Add(command.Reminder);return(DesktopRevision(command.Reminder),Array.Empty<Guid>());
    });
    public DesktopWriteResult DesktopSaveNeed(UserIdentity actor,DesktopNeedCommand command)=>DesktopExecute(actor,command.OperationId,"purchase-need",command,(next,p)=>{
        MobileAccess.Require(p,"view.purchases","purchases.edit");var old=next.Prompt12.PurchaseNeeds.FirstOrDefault(x=>x.Id==command.Need.Id);DesktopExpected(old,command.ExpectedRevision);
        if(old!=null&&old.Status!=PurchaseNeedStatus.ToBuy)throw new WebRequestException(422,"Somente necessidades pendentes podem ser editadas.");
        if(command.Need.Status!=PurchaseNeedStatus.ToBuy)throw new WebRequestException(422,"Necessidade deve permanecer pendente.");if(command.Need.SupplierId is Guid sid&&!next.Prompt12.Suppliers.Any(x=>x.Id==sid))command.Need.SupplierId=null;
        if(old!=null)next.Prompt12.PurchaseNeeds.Remove(old);next.Prompt12.PurchaseNeeds.Add(command.Need);return(DesktopRevision(command.Need),Array.Empty<Guid>());
    });
    DesktopWriteResult DesktopExecute<T>(UserIdentity actor,Guid op,string kind,T command,Func<AppData,Dictionary<string,bool>,(string Revision,Guid[] Sessions)> mutate,Func<JsonElement?>? value=null,Func<BusinessArtifact?>? artifact=null,Func<Action<AppData>?>? finalAudit=null)=>WithLock(()=>{
        BusinessArtifact.Recover(this);
        if(op==Guid.Empty)throw new WebRequestException(400,"operationId obrigatório.");using var db=Open();var p=Permissions.Resolve(db,actor);
        // Re-authorize replays as well as fresh writes using the current session permissions.
        if(kind=="business" && command is BusinessCommand business)BusinessAuthorize(db,actor,p,business);
        else MobileAccess.Require(p,kind switch{"sale"=>"view.cash","order" or "payment"=>"view.orders","customer"=>"view.customers","notes"=>"system.settings","product"=>"view.products","reminder"=>"view.reminders",_=>"view.purchases"});
        if(kind=="payment")MobileAccess.Require(p,"view.cash","finance.receive");
        if(kind=="sale")MobileAccess.Require(p,"finance.receive");
        if(kind=="customer")MobileAccess.Require(p,"customers.edit");
        if(kind=="product")MobileAccess.Require(p,"products.edit");
        if(kind=="purchase-need")MobileAccess.Require(p,"purchases.edit");
        if(kind=="order" && command is DesktopOrderCommand orderWrite)MobileAccess.Require(p,orderWrite.ExpectedRevision.HasValue?"orders.edit":"orders.create");
        if(kind=="reminder" && command is DesktopReminderCommand reminderWrite){
            var original=ReadUnsafe(db,includeAudit:false).Reminders?.FirstOrDefault(x=>x.Id==reminderWrite.Id);
            object? content(Reminder? x)=>x==null?null:new{x.Id,x.Phone,x.Name,x.Title,x.Description,x.DueAt,x.CreatedAt,x.CreatedBy,x.DoneAt};
            if(DesktopRevision(content(original))!=DesktopRevision(content(reminderWrite.Reminder)))MobileAccess.Require(p,"reminders.edit");
        }
        var hash=DesktopRevision(new{contract="desktop-413-v1",kind,command});
        using(var receipt=WebQueries.Command(db,"SELECT actor_id,payload_hash FROM remote_operations WHERE id=@id",("@id",op)))using(var r=receipt.ExecuteReader())if(r.Read()){
            if(r.GetString(0)!=actor.UserId.ToString()||r.GetString(1)!=hash)throw new WebRequestException(409,"operationId já utilizado com outro conteúdo.");r.Close();
            var stored=JsonSerializer.Deserialize<DesktopWriteResult>(WebQueries.Metadata(db,"mobile_result_"+op.ToString("N"),"null"))??throw new WebRequestException(409,"Resultado original indisponível.");MobileAccess.Require(p,stored.RequiredPermissions??[]);return stored with{Replayed=true};
        }
        var clock=System.Diagnostics.Stopwatch.StartNew();var current=ReadForWrite();var tRead=clock.ElapsedMilliseconds;var next=ServerAuthorization.CloneForWrite(current);var tClone=clock.ElapsedMilliseconds;
        (string Revision,Guid[] Sessions) result;var requiredPermissions=new HashSet<string>(StringComparer.Ordinal);
        try{result=mutate(next,p);foreach(var payment in next.ScheduledOrders.SelectMany(x=>x.Payments).Where(x=>current.ScheduledOrders.SelectMany(o=>o.Payments).All(y=>y.Id!=x.Id)))payment.OperationId=op;
            foreach(var correction in next.FinancialCorrections.Where(x=>current.FinancialCorrections.All(y=>y.Id!=x.Id)))
                AuditService.Record(next,new(actor.Name,actor.Role){Permissions=p},"Ajuste financeiro vinculado","Lançamento",correction.AdjustmentSaleId.ToString(),"","",correction.Reason,"",correction.AdjustmentSaleId);
            ServerAuthorization.ValidateAndAudit(current,next,actor,p,key=>requiredPermissions.Add(key),allowClosedValues:kind=="business"&&command is BusinessCommand valuesCommand&&valuesCommand.Action=="cash.values");next=Normalize(next);FinancialIntegrity.ValidateTransition(current,next);}
        catch(InvalidOperationException ex){throw new WebRequestException(422,ex.Message);}
        if(command is DesktopReminderCommand reminderResult)result=(DesktopRevision(next.Reminders?.FirstOrDefault(x=>x.Id==reminderResult.Id)),result.Sessions);
        finalAudit?.Invoke()?.Invoke(next);
        var response=new DesktopWriteResult(op,current.DataVersion+1,result.Revision,result.Sessions,Value:value?.Invoke(),RequiredPermissions:requiredPermissions.Order().ToArray());
        var tCheck=clock.ElapsedMilliseconds;CreateRoutineBackupUnsafe();artifact?.Invoke()?.Publish();SaveUnsafe(next,op,actor.UserId.ToString(),hash,JsonSerializer.Serialize(response));artifact?.Invoke()?.Committed();
        LogSlowWrite(kind,tRead,tClone,tCheck,clock.ElapsedMilliseconds);return response;
    });
    // Medição para saber onde o salvar gasta tempo (auditoria 11/11/2026): só anota gravações de 500 ms ou mais.
    // shortcut: o arquivo não é rotacionado (~110 bytes por linha); apague-o ou remova a medição depois de otimizar.
    void LogSlowWrite(string kind,long read,long clone,long check,long total)
    {
        if(total<500)return;
        try{File.AppendAllText(Path.Combine(Folder,"gravacoes-lentas.log"),$"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {kind}: ler {read} ms, copiar {clone-read} ms, validar {check-clone} ms, gravar {total-check} ms, total {total} ms{Environment.NewLine}");}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public AppData DesktopContext(UserIdentity actor,string kind,Guid id)=>ReadWork(db=>{
        if(kind is not ("order" or "product" or "fiscal"))throw new WebRequestException(404,"Contexto inexistente.");
        var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,kind=="product"?"view.products":"view.orders");
        using var owner=WebQueries.Command(db,"SELECT owner_until FROM auth_sessions WHERE id=@s",("@s",actor.SessionId));
        // Desempenho (pacote 11): recorta a leitura compartilhada ANTES da cópia de permissões (antes copiava o banco
        // inteiro para depois descartar quase tudo). Os recortes abaixo continuam iguais e o resultado é o mesmo.
        var shared=ReadShared(db);var slim=new AppData();
        foreach(var property in typeof(AppData).GetProperties())if(property.CanRead&&property.CanWrite&&property.GetIndexParameters().Length==0)property.SetValue(slim,property.GetValue(shared));
        var target=shared.ScheduledOrders.Where(x=>x.Id==id).ToList();var first=target.FirstOrDefault();
        var keepProducts=(first?.Items??[]).Where(x=>x.ProductId.HasValue).Select(x=>x.ProductId!.Value).ToHashSet();
        var keepSales=(first?.Payments??[]).Where(x=>x.SaleId.HasValue).Select(x=>x.SaleId!.Value).Append(first?.DepositSaleId??Guid.Empty).Append(first?.FinalSaleId??Guid.Empty).ToHashSet();
        slim.ScheduledOrders=target;slim.Products=shared.Products.Where(x=>keepProducts.Contains(x.Id)).ToList();
        slim.Customers=shared.Customers.Where(x=>first!=null&&x.Name.Equals(first.CustomerName,StringComparison.CurrentCultureIgnoreCase)).ToList();
        slim.Sessions=shared.Sessions.Where(x=>x.Sales.Any(s=>keepSales.Contains(s.Id))).ToList();
        slim.AuditTrail=[];slim.ManualDebts=[];slim.Prospects=[];slim.CollectionContacts=[];slim.LostEntries=[];slim.CashClosureVersions=[];slim.Reminders=[];
        var data=ServerAuthorization.View(slim,p,Convert.ToInt64(owner.ExecuteScalar())>DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if(!p.GetValueOrDefault("costs.edit"))foreach(var product in data.Products){product.Cost=null;foreach(var price in product.ExactQuantityPrices)price.TotalCost=null;foreach(var tier in product.QuantityPriceTiers)tier.UnitCost=null;}
        data.AuditTrail=[];data.ManualDebts=[];data.Prospects=[];data.CollectionContacts=[];data.LostEntries=[];data.CashClosureVersions=[];data.Reminders=[];
        data.ScheduledOrders=data.ScheduledOrders.Where(x=>x.Id==id).ToList();var order=data.ScheduledOrders.FirstOrDefault();
        var productIds=(order?.Items??[]).Where(x=>x.ProductId.HasValue).Select(x=>x.ProductId!.Value).ToHashSet();
        data.Products=data.Products.Where(x=>productIds.Contains(x.Id)).ToList();
        data.Customers=data.Customers.Where(x=>order!=null&&x.Name.Equals(order.CustomerName,StringComparison.CurrentCultureIgnoreCase)).ToList();
        var saleIds=(order?.Payments??[]).Where(x=>x.SaleId.HasValue).Select(x=>x.SaleId!.Value).Append(order?.DepositSaleId??Guid.Empty).Append(order?.FinalSaleId??Guid.Empty).ToHashSet();
        data.Sessions=data.Sessions.Where(x=>x.Sales.Any(s=>saleIds.Contains(s.Id))).ToList();foreach(var session in data.Sessions)session.Sales=session.Sales.Where(s=>saleIds.Contains(s.Id)).ToList();
        if(kind=="product"){data.Customers=[];data.BudgetRules=[];}
        // Notas do pedido: quem pode registrar NF (system.owner) vê as notas deste pedido sem o PIN do dono (escolha do usuário, 26/10);
        // antes a NF gravava mas a lista ficava vazia sem o PIN. As outras áreas do dono continuam com PIN.
        if(kind=="fiscal"){data.Products=[];data.Customers=data.Customers.Where(x=>order!=null&&x.Name.Equals(order.CustomerName,StringComparison.CurrentCultureIgnoreCase)).ToList();data.Prompt12.FiscalDocuments=p.GetValueOrDefault("system.owner")?shared.Prompt12.FiscalDocuments.Where(x=>x.OrderId==id).Select(x=>JsonSerializer.Deserialize<FiscalDocument>(JsonSerializer.Serialize(x))!).ToList():[];}
        data.Prompt12.PurchaseNeeds=[];data.Prompt12.Purchases=[];data.Prompt12.Losses=[];data.Prompt12.Expenses=[];data.Prompt12.RetiredProfessionalQuotes=[];data.Prompt12.Reworks=[];data.Prompt12.Outsourced=[];
        if(kind!="fiscal")data.Prompt12.FiscalDocuments=[];
        if(kind=="fiscal"){data.Prompt12.Materials=[];data.Prompt12.KnownYields=[];data.Prompt12.CommunicationTemplates=[];}
        return data;
    });
    public Reminder DesktopReminder(UserIdentity actor,Guid id)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.reminders");return ReadUnsafe(db,includeAudit:false).Reminders?.FirstOrDefault(x=>x.Id==id)??throw new WebRequestException(404,"Lembrete não encontrado.");});
    public PurchaseNeed DesktopNeed(UserIdentity actor,Guid id)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.purchases");return ReadUnsafe(db,includeAudit:false).Prompt12.PurchaseNeeds.FirstOrDefault(x=>x.Id==id)??throw new WebRequestException(404,"Necessidade não encontrada.");});
    public DesktopNotesState DesktopNotes(UserIdentity actor)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"system.settings");var data=ReadUnsafe(db,includeAudit:false);return new DesktopNotesState(data.ImprovementNotes,DesktopRevision(data.ImprovementNotes));});
    public DesktopWriteResult RemoteDesktopWrite(string path,object command)=>remote?.DesktopWrite(path,command)??throw new InvalidOperationException("Comando remoto exige conexão.");
    public AppData RemoteDesktopContext(string kind,Guid id=default)=>QueryRemoteAsync<AppData>($"desktop/context/{kind}/{id}").GetAwaiter().GetResult();
}
