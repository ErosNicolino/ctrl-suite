using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GraficaCaixa.Wpf;

// Compras em cartões: cada material com quantidade em destaque, prazo (faixa colorida), prioridade e os botões no próprio cartão.
public sealed class PurchaseNeedsWindow : Window
{
    bool writeBusy;
    enum PurchaseFilter { Pending, Completed, All }
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");

    readonly DataStore store;
    readonly TextBox searchBox=new(){ToolTip="Digite parte do nome do material ou do fornecedor",Tag="Buscar material ou fornecedor…"};
    readonly WrapPanel cards=new();readonly ViewToggle view=new(){Mode="Cards",StorageKey="compras",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};
    readonly StackPanel summary=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock emptyTitle=new(),emptyHint=new();readonly StackPanel emptyState=new();
    readonly Dictionary<PurchaseFilter,Button> filterButtons=[];
    PurchaseFilter filter=PurchaseFilter.Pending;
    List<Purchase> purchases=[];Prompt12State state=new();readonly ModuleLoader remoteLoad;

    public PurchaseNeedsWindow(DataStore store)
    {
        this.store=store;Title="Compras";Width=1260;Height=760;MinWidth=1050;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Margin=new Thickness(24,22,24,18)};Content=root;
        foreach(var h in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})root.RowDefinitions.Add(new RowDefinition{Height=h});

        var heading=new DockPanel{Margin=new Thickness(0,0,0,18)};
        var add=new Button{Content="+ Nova necessidade",Style=ResourceStyle("PrimaryButton"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0)};add.Click+=(_,_)=>Add();
        DockPanel.SetDock(add,Dock.Right);heading.Children.Add(add);DockPanel.SetDock(summary,Dock.Right);heading.Children.Add(summary);
        var titles=new StackPanel();titles.Children.Add(new TextBlock{Text="Compras",Style=ResourceStyle("Title")});
        titles.Children.Add(new TextBlock{Text="O que precisa ser comprado, até quando e para quem. Clique em \"Comprado\" quando chegar.",Foreground=ThemeManager.Get("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)});
        heading.Children.Add(titles);root.Children.Add(heading);

        var tools=new Grid{Margin=new Thickness(0,0,0,16)};tools.ColumnDefinitions.Add(new ColumnDefinition());tools.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});tools.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});Grid.SetColumn(view,2);tools.Children.Add(view);view.ModeChanged+=_=>Refresh();cards.SizeChanged+=(_,_)=>{if(view.Mode=="Lista")cards.ItemWidth=Math.Max(300,cards.ActualWidth-4);};
        var filters=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var (value,text) in new[]{(PurchaseFilter.Pending,"Para comprar"),(PurchaseFilter.Completed,"Comprados"),(PurchaseFilter.All,"Todos")})
        {var b=new Button{Content=text,Style=ResourceStyle("SoftButton"),Margin=new Thickness(0,0,8,0)};b.Click+=(_,_)=>SetFilter(value);filterButtons[value]=b;filters.Children.Add(b);}
        tools.Children.Add(filters);searchBox.TextChanged+=(_,_)=>Refresh();SearchShortcut.SetIsTarget(searchBox,true);Grid.SetColumn(searchBox,1);tools.Children.Add(searchBox);
        Grid.SetRow(tools,1);root.Children.Add(tools);

        var area=new Grid();Grid.SetRow(area,2);root.Children.Add(area);
        area.Children.Add(new ScrollViewer{Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        emptyTitle.Style=ResourceStyle("SectionTitle");emptyTitle.HorizontalAlignment=HorizontalAlignment.Center;
        emptyHint.Foreground=ThemeManager.Get("Muted");emptyHint.Margin=new Thickness(0,7,0,0);emptyHint.TextAlignment=TextAlignment.Center;
        emptyState.HorizontalAlignment=HorizontalAlignment.Center;emptyState.VerticalAlignment=VerticalAlignment.Center;emptyState.Children.Add(emptyTitle);emptyState.Children.Add(emptyHint);area.Children.Add(emptyState);

        remoteLoad=new(this,store);Loaded+=(_,_)=>Refresh();
        SetFilter(PurchaseFilter.Pending);
    }

    static bool IsPending(PurchaseNeed need)=>need.Status==PurchaseNeedStatus.ToBuy;
    static int DaysLeft(PurchaseNeed need)=>need.BuyBy is DateTime due?(due.Date-DateTime.Today).Days:int.MaxValue;

    void SetFilter(PurchaseFilter value)
    {
        filter=value;
        foreach(var pair in filterButtons){pair.Value.SetResourceReference(BackgroundProperty,pair.Key==filter?"AccentSoft":"Surface");pair.Value.SetResourceReference(ForegroundProperty,pair.Key==filter?"AccentInk":"Ink");pair.Value.SetResourceReference(BorderBrushProperty,pair.Key==filter?"Accent":"Line");}
        Refresh();
    }

    async void Refresh()
    {
        if(remoteLoad==null||!IsLoaded)return;List<PurchaseNeed> needs;try{var data=store.IsRemote?await remoteLoad.LoadAsync("compras",refresh:Refresh,debounce:true):store.Load();if(data==null)return;state=data.Prompt12;needs=state.PurchaseNeeds;purchases=state.Purchases??[];}catch(Exception){return;}
        var pending=needs.Where(IsPending).ToList();var overdue=pending.Count(x=>DaysLeft(x)<0);var soon=pending.Count(x=>DaysLeft(x) is var dl && dl>=0 && dl<=GraficaCaixa.BusinessRules.Current.PriorityDays);
        summary.Children.Clear();
        void Pill(string text,string bg,string fg){summary.Children.Add(new Border{Background=ThemeManager.Get(bg),CornerRadius=new CornerRadius(12),Padding=new Thickness(11,4,11,4),Margin=new Thickness(6,0,0,0),Child=new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,FontSize=13,Foreground=ThemeManager.Get(fg)}});}
        var month=purchases.Where(x=>x.Date.Year==DateTime.Today.Year&&x.Date.Month==DateTime.Today.Month).Sum(x=>x.Amount);
        Pill($"Gasto em {DateTime.Today.ToString("MMMM",Pt)}: {month.ToString("C2",Pt)}","KindCompraSoft","Ink");
        Pill($"{pending.Count} para comprar","SurfaceAlt","Ink");if(overdue>0)Pill($"{overdue} atrasada(s)","DangerSoft","Danger");if(soon>0)Pill($"{soon} vencem em até {GraficaCaixa.BusinessRules.Current.PriorityDays} dias","WarningSoft","Warning");
        filterButtons[PurchaseFilter.Pending].Content=$"Para comprar ({pending.Count})";filterButtons[PurchaseFilter.Completed].Content=$"Comprados ({needs.Count-pending.Count})";
        var term=searchBox.Text.Trim();
        var visible=needs.Where(x=>filter==PurchaseFilter.All||filter==PurchaseFilter.Pending&&IsPending(x)||filter==PurchaseFilter.Completed&&!IsPending(x))
            .Where(x=>term.Length==0||x.Material.Contains(term,StringComparison.CurrentCultureIgnoreCase)||x.Notes.Contains(term,StringComparison.CurrentCultureIgnoreCase)||x.Supplier.Contains(term,StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x=>IsPending(x)?0:1).ThenBy(DaysLeft).ThenByDescending(x=>x.Priority).ThenBy(x=>x.Material,StringComparer.CurrentCultureIgnoreCase).ToList();
        var listMode=view.Mode=="Lista";cards.ItemWidth=listMode?Math.Max(300,cards.ActualWidth-4):double.NaN;cards.Children.Clear();foreach(var need in visible)cards.Children.Add(Card(need,listMode));
        emptyState.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
        emptyTitle.Text=term.Length>0?"Nenhum material encontrado.":filter switch{PurchaseFilter.Pending=>"Nada para comprar agora",PurchaseFilter.Completed=>"Nenhuma compra concluída ainda.",_=>"Nenhuma necessidade de compra cadastrada."};
        emptyHint.Text=term.Length>0?"Tente outro nome de material.":"Use \"+ Nova necessidade\" para anotar algo que precisa ser comprado.";
    }

    // UX 2.0 (pacote 3): cartão com filete de prazo nas cores do sistema (vermelho atrasado, âmbar perto, verde comprado),
    // prioridade como etiqueta com ícone e ações com rótulo. Em "Lista" o mesmo item vira uma linha. Mesmas ações de antes.
    Border Card(PurchaseNeed need,bool listMode=false)
    {
        var pending=IsPending(need);var days=DaysLeft(need);
        var (stripe,alert,alertKey)=!pending?("COk","Comprado","AccentInk"):days switch{<0=>("CDanger",$"Atrasado há {-days} dia(s)","Danger"),0=>("CWarn","Para hoje","Warning"),var pr when pr<=GraficaCaixa.BusinessRules.Current.PriorityDays=>("CWarn",days==1?"Amanhã":$"Em {days} dias","Warning"),int.MaxValue=>("Line","Sem prazo","Muted"),_=>("KBuy",$"Em {days} dias","Muted")};
        var card=new Border{Width=listMode?double.NaN:310,MinHeight=listMode?0:180,Margin=new Thickness(0,0,listMode?0:12,listMode?8:12),CornerRadius=new CornerRadius(listMode?10:14),BorderThickness=new Thickness(5,1,1,1),Padding=new Thickness(15,12,12,12),Opacity=pending?1:.7,ToolTip=pending?"Dois cliques para editar":null};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,stripe);if(listMode){card.BorderThickness=new Thickness(1);card.SetResourceReference(Border.BorderBrushProperty,"Line");card.Padding=new Thickness(0,10,12,10);}
        var (priority,tone)=need.Priority switch{PurchasePriority.Urgent=>("Urgente","late"),PurchasePriority.High=>("Alta","now"),PurchasePriority.Low=>("Baixa","neutral"),_=>("Normal","neutral")};
        var chip=new StatusTag{Text=priority,Tone=tone,VerticalAlignment=VerticalAlignment.Top};if(!pending)chip.Visibility=Visibility.Collapsed;
        var title=new TextBlock{Text=need.Material,FontSize=listMode?15:16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextDecorations=pending?null:TextDecorations.Strikethrough};
        var amount=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};var qty=new TextBlock{Text=need.Quantity.ToString("0.##",Pt),FontSize=listMode?18:24,FontWeight=FontWeights.SemiBold};qty.SetResourceReference(TextBlock.ForegroundProperty,"KindCompra");amount.Children.Add(qty);
        var unit=new TextBlock{Text=string.IsNullOrWhiteSpace(need.Unit)?"un":need.Unit,FontSize=14,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(6,0,0,listMode?2:4)};unit.SetResourceReference(TextBlock.ForegroundProperty,"Muted");amount.Children.Add(unit);
        var when=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var cal=new UiIcon{IconName="calendar-days",Width=15,Height=15,VerticalAlignment=VerticalAlignment.Center};cal.SetResourceReference(UiIcon.ForegroundProperty,"Muted");when.Children.Add(cal);
        var dueText=new TextBlock{Text=need.BuyBy is DateTime due?$"Até {due.ToString("ddd, dd/MM",Pt)}":"Sem data",FontSize=12,Margin=new Thickness(5,0,8,0),VerticalAlignment=VerticalAlignment.Center};dueText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");when.Children.Add(dueText);
        var alertText=new TextBlock{Text=alert,FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};alertText.SetResourceReference(TextBlock.ForegroundProperty,alertKey);when.Children.Add(alertText);
        var details=new StackPanel();
        if(pending&&!string.IsNullOrWhiteSpace(need.Supplier)){var sup=new TextBlock{Text="Fornecedor: "+(SupplierBook.Find(state,need.SupplierId,need.Supplier)?.Name??need.Supplier),FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4),TextWrapping=TextWrapping.Wrap};details.Children.Add(sup);}
        if(!string.IsNullOrWhiteSpace(need.Notes))details.Children.Add(new TextBlock{Text=need.Notes,TextWrapping=TextWrapping.Wrap,FontSize=13,MaxHeight=54,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=need.Notes});
        if(!pending&&purchases.Where(x=>x.NeedId==need.Id).OrderByDescending(x=>x.Date).FirstOrDefault() is Purchase paid){var p=new TextBlock{Text=$"Pago {paid.Amount.ToString("C2",Pt)}"+(paid.Supplier.Length>0?$" • {paid.Supplier}":"")+$" • {paid.Date:dd/MM}",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4),TextWrapping=TextWrapping.Wrap};p.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");details.Children.Add(p);}
        if(!string.IsNullOrWhiteSpace(need.RequestedBy)){var r=new TextBlock{Text=$"Pedido por {need.RequestedBy}",FontSize=12,Margin=new Thickness(0,4,0,0)};r.SetResourceReference(TextBlock.ForegroundProperty,"Muted");details.Children.Add(r);}
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        Button Labeled(string label,string tip,Action action){var b=new Button{Content=label,Style=ResourceStyle("SmallButton"),Margin=new Thickness(6,0,0,0),ToolTip=tip};b.Click+=(_,_)=>action();actions.Children.Add(b);return b;}
        if(pending){var bought=new Button{Content="Comprado",Style=ResourceStyle("PrimaryButton"),Padding=new Thickness(12,4,12,4),MinHeight=32,FontSize=13,ToolTip="Marcar como comprado e informar quanto pagou"};bought.SetResourceReference(BackgroundProperty,"KindCompra");bought.SetResourceReference(Ui.HoverBackgroundProperty,"KindCompra");bought.SetResourceReference(ForegroundProperty,"OnK");bought.Click+=(_,_)=>Complete(need.Id);actions.Children.Add(bought);Labeled("Editar","Editar necessidade",()=>Edit(need.Id));}
        else Labeled("Reabrir","Voltar para \"para comprar\"",()=>Reopen(need.Id));
        Labeled("Custo","Lançar custo no cadastro de produtos",OpenProducts);
        var trashButton=new Button{Style=ResourceStyle("IconButtonSmall"),Tag="trash",ToolTip="Excluir",Margin=new Thickness(4,0,0,0)};trashButton.SetResourceReference(ForegroundProperty,"Danger");trashButton.Click+=(_,_)=>Delete(need.Id);actions.Children.Add(trashButton);
        var grid=new Grid();card.Child=grid;
        if(listMode)
        {
            foreach(var w in new[]{new GridLength(1.3,GridUnitType.Star),new GridLength(110),new GridLength(1.6,GridUnitType.Star),new GridLength(96),new GridLength(1.2,GridUnitType.Star),GridLength.Auto})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=w});var mark=new Border{Width=4,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(-1,-10,0,-10)};mark.SetResourceReference(Border.BackgroundProperty,stripe);grid.Children.Add(mark);title.Margin=new Thickness(15,0,0,0);
            title.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(title);Grid.SetColumn(amount,1);amount.Margin=new Thickness(10,0,0,0);grid.Children.Add(amount);Grid.SetColumn(when,2);grid.Children.Add(when);chip.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(chip,3);grid.Children.Add(chip);details.VerticalAlignment=VerticalAlignment.Center;details.Margin=new Thickness(8,0,8,0);Grid.SetColumn(details,4);grid.Children.Add(details);Grid.SetColumn(actions,5);grid.Children.Add(actions);
        }
        else
        {
            foreach(var h in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})grid.RowDefinitions.Add(new RowDefinition{Height=h});
            var top=new DockPanel();DockPanel.SetDock(chip,Dock.Right);chip.Margin=new Thickness(8,2,0,0);top.Children.Add(chip);top.Children.Add(title);grid.Children.Add(top);
            amount.Margin=new Thickness(0,6,0,0);Grid.SetRow(amount,1);grid.Children.Add(amount);when.Margin=new Thickness(0,4,0,0);Grid.SetRow(when,2);grid.Children.Add(when);
            details.Margin=new Thickness(0,8,0,8);Grid.SetRow(details,3);grid.Children.Add(details);Grid.SetRow(actions,4);grid.Children.Add(actions);
        }
        if(pending)card.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)Edit(need.Id);};
        return card;
    }

    // Atalho "+ Novo → Compra" (UX 2.0): abre a mesma inclusão do botão "+ Nova necessidade".
    public void StartNew()=>Add();
    async void Add()
    {
        if(writeBusy)return;writeBusy=true;try{
        if(await CreateNeedAsync(this,store,state,null))SetFilter(PurchaseFilter.Pending);
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

    // Nova necessidade (botão, atalho "+ Novo" e "Nova compra deste fornecedor"): grava no servidor quando remoto.
    internal static async Task<bool> CreateNeedAsync(Window owner,DataStore store,Prompt12State state,string? presetSupplier)
    {
        var dialog=new NeedDialog(null,state,store,presetSupplier){Owner=owner};
        if(dialog.ShowDialog()!=true||dialog.Result is not PurchaseNeed need)return false;
        var saved=false;
        await Prompt12Ui.Run(owner,async ()=>
        {
            if(store.IsRemote)await store.RemoteDesktopWriteAsync("desktop/purchase-needs",new DesktopNeedCommand(Guid.NewGuid(),null,need));else store.Update(data=>{data.Prompt12.PurchaseNeeds.Add(need);AuditService.Record(data,App.CurrentUser,"Necessidade de compra criada","Compra",need.Id.ToString(),"",need.Material,"",need.RequestedBy);return 0;});
            saved=true;
        });
        return saved;
    }

    async void Edit(Guid id)
    {
        if(writeBusy)return;writeBusy=true;try{
        var current=store.IsRemote?await store.QueryRemoteAsync<PurchaseNeed>($"desktop/purchase-needs/{id}"):store.Load().Prompt12.PurchaseNeeds.FirstOrDefault(x=>x.Id==id);
        if(current is null||!IsPending(current)){Refresh();return;}
        var dialog=new NeedDialog(current,state,store){Owner=this};
        if(dialog.ShowDialog()!=true||dialog.Result is null)return;
        await Prompt12Ui.Run(this,async ()=>
        {
            if(store.IsRemote)await store.RemoteDesktopWriteAsync("desktop/purchase-needs",new DesktopNeedCommand(Guid.NewGuid(),DataStore.DesktopRevision(current),dialog.Result));else store.Update(data=>
            {
                var target=data.Prompt12.PurchaseNeeds.Single(x=>x.Id==id);
                if(!IsPending(target))throw new InvalidOperationException("Somente necessidades pendentes podem ser editadas.");
                var oldValue=$"{target.Material} | {target.Quantity} {target.Unit} | {target.BuyBy:dd/MM/yyyy} | {target.Supplier}";
                target.Material=dialog.Result.Material;target.Quantity=dialog.Result.Quantity;target.Unit=dialog.Result.Unit;target.Priority=dialog.Result.Priority;target.BuyBy=dialog.Result.BuyBy;target.Notes=dialog.Result.Notes;target.Supplier=dialog.Result.Supplier;target.SupplierId=dialog.Result.SupplierId;target.RequestedBy=dialog.Result.RequestedBy;
                var newValue=$"{target.Material} | {target.Quantity} {target.Unit} | {target.BuyBy:dd/MM/yyyy} | {target.Supplier}";
                AuditService.Record(data,App.CurrentUser,"Necessidade de compra editada","Compra",target.Id.ToString(),oldValue,newValue,"",target.Material);return 0;
            });
            Refresh();
        });
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

    // "Comprado": pergunta quanto foi pago e registra o gasto junto com a compra.
    async void Complete(Guid id)
    {
        if(writeBusy)return;writeBusy=true;try{
        var need=(await store.BusinessContextAsync("purchase",id)).Prompt12.PurchaseNeeds.FirstOrDefault(x=>x.Id==id);if(need is null||!IsPending(need)){Refresh();return;}
        var dialog=new PurchaseCompleteDialog(need,state,store){Owner=this};if(dialog.ShowDialog()!=true||dialog.Result is not Purchase purchase)return;
        await Prompt12Ui.Run(this,async ()=>
        {
            await BusinessUi.ExecuteAsync(store,"purchase.complete",id,need,new PurchaseCompleteArgs(purchase),()=>store.Update(data=>
            {
                var target=data.Prompt12.PurchaseNeeds.Single(x=>x.Id==id);if(!IsPending(target))return 0;
                target.Status=PurchaseNeedStatus.Received;data.Prompt12.Purchases??=[];data.Prompt12.Purchases.Add(purchase);
                AuditService.Record(data,App.CurrentUser,"Compra concluída","Compra",target.Id.ToString(),PurchaseNeedStatus.ToBuy.ToString(),PurchaseNeedStatus.Received.ToString(),"",$"{target.Material} • {purchase.Quantity.ToString("0.##",Pt)} {target.Unit} • {purchase.Amount.ToString("C2",Pt)}"+(purchase.Supplier.Length>0?$" • {purchase.Supplier}":""));return 0;
            }));
            Refresh();
        });
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

    async void Reopen(Guid id)
    {
        if(writeBusy)return;writeBusy=true;try{
        await Prompt12Ui.Run(this,async ()=>
        {
            await BusinessUi.ExecuteAsync(store,"purchase.reopen",id,(await store.BusinessContextAsync("purchase",id)).Prompt12.PurchaseNeeds.Single(x=>x.Id==id),new EmptyArgs(),()=>store.Update(data=>{var target=data.Prompt12.PurchaseNeeds.Single(x=>x.Id==id);if(IsPending(target))return 0;var before=target.Status;target.Status=PurchaseNeedStatus.ToBuy;data.Prompt12.Purchases?.RemoveAll(x=>x.NeedId==id);AuditService.Record(data,App.CurrentUser,"Compra reaberta","Compra",target.Id.ToString(),before.ToString(),PurchaseNeedStatus.ToBuy.ToString(),"",target.Material);return 0;}));
            Refresh();
        });
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

    async void Delete(Guid id)
    {
        if(writeBusy)return;writeBusy=true;try{
        var need=(await store.BusinessContextAsync("purchase",id)).Prompt12.PurchaseNeeds.FirstOrDefault(x=>x.Id==id);if(need is null){Refresh();return;}
        if(!AlertWindow.Confirm(this,"Excluir necessidade",$"Excluir \"{need.Material}\" da lista de compras?",true))return;
        await Prompt12Ui.Run(this,async ()=>
        {
            await BusinessUi.ExecuteAsync(store,"purchase.delete",id,need,new EmptyArgs(),()=>store.Update(data=>{var target=data.Prompt12.PurchaseNeeds.Single(x=>x.Id==id);data.Prompt12.PurchaseNeeds.Remove(target);AuditService.Record(data,App.CurrentUser,"Necessidade de compra excluída","Compra",target.Id.ToString(),target.Material,"","Exclusão confirmada",target.RequestedBy);return 0;}));
            Refresh();
        });
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }

    void OpenProducts()=>new ProductsWindow(store){Owner=this}.ShowDialog();

    Style ResourceStyle(string key)=>(Style)FindResource(key);
}
