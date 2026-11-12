using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace GraficaCaixa.Wpf;

// Lembretes: pessoas que ainda não foram lançadas no sistema, para verificar depois na conversa do WhatsApp.
// Ficam no banco central (aparecem em todos os computadores). Abre como área do menu lateral.
public sealed class RemindersWindow : Window
{
    public event Action<int>? UnreadCountChanged;
    // Atalho "+ Novo → Lembrete" (UX 2.0): leva o foco ao telefone do formulário de inclusão rápida.
    public void StartNew()=>Dispatcher.BeginInvoke(()=>{contactName.Focus();contactName.SelectAll();},System.Windows.Threading.DispatcherPriority.Input);
    readonly DataStore store;readonly TextBox phone=new(){Tag="Telefone (ex.: 11 95202-8478)"},contactName=new(){Tag="Nome (opcional)",MaxLength=180},description=new(){Tag="O que verificar? Ex.: Verificar quando pagou e como"},searchBox=new(){Width=270,VerticalAlignment=VerticalAlignment.Center,ToolTip="Pesquisar nome, telefone ou texto do lembrete",Tag="Pesquisar nome, telefone ou texto…"};
    readonly WrapPanel cards=new();readonly TextBlock summary=new(),warning=new();readonly EmptyState empty=new(){IconName="bell"};readonly ViewToggle view=new(){Mode="Cards",StorageKey="lembretes",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0)};readonly Button pendingButton,doneButton,addButton;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(30)};bool showDone;bool unreadOnly;Guid Reader=>store.AuthenticatedUser?.UserId??throw new InvalidOperationException("Entre com seu usuário para registrar a leitura.");bool IsRead(Reminder r)=>store.AuthenticatedUser is {} user&&r.ReadByUsers.ContainsKey(user.UserId);
    readonly ModuleLoader remoteLoad;bool polling;string? remoteRevision;AppData? lastData;readonly CancellationTokenSource lifetime=new();
    static readonly string[] CardColors=["AccentSoft","WarningSoft","SurfaceAlt"];

    public RemindersWindow(DataStore store)
    {
        this.store=store;Title="Lembretes";Width=1100;Height=720;
        var root=new Grid{Margin=new Thickness(24,22,24,18)};Content=root;
        foreach(var h in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})root.RowDefinitions.Add(new RowDefinition{Height=h});

        var header=new DockPanel{Margin=new Thickness(0,0,0,16)};
        summary.SetResourceReference(TextBlock.ForegroundProperty,"Muted");summary.VerticalAlignment=VerticalAlignment.Center;var right=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};right.Children.Add(summary);right.Children.Add(view);var newReminder=new Button{Content="+ Novo lembrete",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(14,0,0,0),ToolTip="Lembrete com título, data e horário para avisar e cliente (opcional)"};newReminder.Click+=(_,_)=>NewReminder();right.Children.Add(newReminder);DockPanel.SetDock(right,Dock.Right);header.Children.Add(right);view.ModeChanged+=_=>Refresh();cards.SizeChanged+=(_,_)=>{if(view.Mode=="Lista")cards.ItemWidth=Math.Max(300,cards.ActualWidth-4);};
        var titles=new StackPanel();titles.Children.Add(new TextBlock{Text="Lembretes",Style=(Style)FindResource("Title")});
        titles.Children.Add(new TextBlock{Text="Pessoas para verificar depois na conversa do WhatsApp. Clique no ícone verde para abrir a conversa.",Foreground=ThemeManager.Get("Muted"),Margin=new Thickness(0,4,0,0),TextWrapping=TextWrapping.Wrap});
        header.Children.Add(titles);root.Children.Add(header);

        var add=new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(16)};Grid.SetRow(add,1);root.Children.Add(add);
        var form=new Grid();add.Child=form;
        foreach(var width in new[]{new GridLength(220),new GridLength(10),new GridLength(210),new GridLength(10),new GridLength(1,GridUnitType.Star),GridLength.Auto})form.ColumnDefinitions.Add(new ColumnDefinition{Width=width});
        form.Children.Add(contactName);PhoneInput.Attach(phone);Grid.SetColumn(phone,2);form.Children.Add(phone);Grid.SetColumn(description,4);form.Children.Add(description);
        // Nome: vem sozinho pelo telefone de um cliente cadastrado (ou pelas sugestões ao digitar); quem não é cliente, digita.
        CustomerAutocomplete.Attach(contactName,phone,()=>store.IsRemote?[]:store.Load().Customers,()=>store);
        addButton=new Button{Content="+ Adicionar lembrete",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(10,0,0,0)};addButton.Click+=(_,_)=>Add();Grid.SetColumn(addButton,5);form.Children.Add(addButton);
        foreach(var box in new[]{contactName,phone,description})box.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;if(box==contactName){if(PhoneText.Digits(phone.Text).Length>=8)description.Focus();else phone.Focus();}else if(box==phone)description.Focus();else Add();}};
        warning.Foreground=ThemeManager.Get("Warning");warning.FontWeight=FontWeights.SemiBold;warning.TextWrapping=TextWrapping.Wrap;warning.Visibility=Visibility.Collapsed;warning.Margin=new Thickness(0,10,0,0);

        var tools=new DockPanel{Margin=new Thickness(0,16,0,12)};Grid.SetRow(tools,2);root.Children.Add(tools);DockPanel.SetDock(searchBox,Dock.Right);searchBox.Margin=new Thickness(12,0,0,0);tools.Children.Add(searchBox);searchBox.TextChanged+=(_,_)=>{if(lastData!=null)Draw(lastData);};SearchShortcut.SetIsTarget(searchBox,true);
        var filters=new StackPanel{Orientation=Orientation.Horizontal};tools.Children.Add(filters);
        Button Filter(string text,bool done){var b=new Button{Content=text,Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,0,8,0)};b.Click+=(_,_)=>{showDone=done;Refresh();};filters.Children.Add(b);return b;}
        pendingButton=Filter("Pendentes",false);doneButton=Filter("Concluídos",true);var unread=new CheckBox{Content="Não lidos",Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};unread.Click+=(_,_)=>{unreadOnly=unread.IsChecked==true;Refresh();};filters.Children.Add(unread);filters.Children.Add(warning);

        var area=new Grid();Grid.SetRow(area,3);root.Children.Add(area);
        area.Children.Add(new ScrollViewer{Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        area.Children.Add(empty);empty.Action+=(_,_)=>{searchBox.Clear();searchBox.Focus();};

        remoteLoad=new(this,store);Loaded+=(_,_)=>{Refresh();contactName.Focus();timer.Start();};timer.Tick+=(_,_)=>Poll();Closed+=(_,_)=>{timer.Stop();lifetime.Cancel();};
    }

    async void Run(Func<Task> action){try{await action();}catch(Exception ex){AlertWindow.Info(this,"Lembretes",ex.Message);}}
    void Run(Action action){try{action();}catch(Exception ex){AlertWindow.Info(this,"Lembretes",ex.Message);}}
    void Add()=>Run(async ()=>
    {
        var digits=PhoneText.Digits(phone.Text);
        if(digits.Length==0||!PhoneText.IsCompleteOrEmpty(phone.Text)){phone.Focus();throw new InvalidOperationException("Informe o telefone com DDD (ex.: 11 95202-8478).");}
        if(string.IsNullOrWhiteSpace(description.Text)){description.Focus();throw new InvalidOperationException("Escreva o que precisa verificar.");}
        var text=description.Text.Trim();var normalized=PhoneText.Normalize(phone.Text);var typedName=contactName.Text.Trim();
        var unreadCount=store.IsRemote?await AddRemoteReminder(normalized,typedName,text):store.Update(d=>{d.Reminders??=[];d.Reminders.Add(new Reminder{Phone=normalized,Name=typedName.Length>0?typedName:NameFor(d,normalized),Description=text,CreatedBy=Environment.MachineName});AuditService.Record(d,App.CurrentUser,"Lembrete criado","Lembrete",normalized,"",text,"",$"{PhoneText.Format(normalized)} — {text}");return 0;});
        phone.Clear();contactName.Clear();description.Clear();showDone=false;Refresh();contactName.Focus();
    });
    // Novo lembrete completo (título, quando avisar, cliente opcional).
    void NewReminder()=>Run(async ()=>
    {
        var dialog=new ReminderDialog{Owner=this};if(dialog.ShowDialog()!=true||dialog.Result is not Reminder r)return;
        if(store.IsRemote)await store.RemoteDesktopWriteAsync("desktop/reminders",new DesktopReminderCommand(Guid.NewGuid(),r.Id,null,r));
        else store.Update(d=>{d.Reminders??=[];if(r.Name.Length==0)r.Name=NameFor(d,r.Phone);d.Reminders.Add(r);AuditService.Record(d,App.CurrentUser,"Lembrete criado","Lembrete",r.Id.ToString(),"",r.Title,"",$"{r.Title} — {ReminderDialog.When(r.DueAt)}");return 0;});
        showDone=false;Refresh();
    });
    async Task<int> AddRemoteReminder(string phone,string name,string text){var draft=new Reminder{Phone=phone,Name=name,Description=text,CreatedBy=Environment.MachineName};await store.RemoteDesktopWriteAsync("desktop/reminders",new DesktopReminderCommand(Guid.NewGuid(),draft.Id,null,draft));return 0;}
    static string NameFor(AppData data,string phone){var digits=PhoneText.Digits(phone);return digits.Length<8?"":data.Customers.FirstOrDefault(c=>{var d=PhoneText.Digits(c.Contact);return d.Length>=8&&(d.EndsWith(digits)||digits.EndsWith(d));})?.Name??"";}

    async void Poll(){if(!IsVisible||polling||remoteLoad.Loading)return;if(!store.IsRemote){Refresh();return;}polling=true;try{var revision=await store.QueryRemoteAsync<RemoteRevision>("api/v1/revision",lifetime.Token);if(revision.Revision!=remoteRevision){remoteRevision=revision.Revision;Refresh();}}catch{}finally{polling=false;}}
    async void Refresh()
    {
        AppData? data;try{data=store.IsRemote?await remoteLoad.LoadAsync("lembretes",refresh:Refresh):store.Load();if(data==null)return;}catch(Exception ex){summary.Text=ex.Message;return;}
        lastData=data;remoteRevision=data.DataVersion.ToString();var supported=data.Reminders!=null;addButton.IsEnabled=supported;
        warning.Text="O servidor do PC1 ainda está na versão antiga: reinicie o servidor (Reiniciar_Servidor_ERP.cmd) para usar os lembretes.";warning.Visibility=supported?Visibility.Collapsed:Visibility.Visible;
        var all=data.Reminders??[];UnreadCountChanged?.Invoke(all.Count(x=>x.DoneAt==null&&!IsRead(x)));var pending=all.Count(x=>x.DoneAt==null);
        summary.Text=pending==0?"Nenhum pendente":$"{pending} pendente(s)";
        pendingButton.Content=$"Pendentes ({pending})";doneButton.Content=$"Concluídos ({all.Count-pending})";
        pendingButton.Background=ThemeManager.Get(showDone?"SoftButtonBg":"AccentSoft");doneButton.Background=ThemeManager.Get(showDone?"AccentSoft":"SoftButtonBg");
        Draw(data);
    }
    // Só redesenha com os dados já carregados: digitar na pesquisa não consulta o servidor.
    void Draw(AppData data)
    {
        var all=data.Reminders??[];var term=searchBox.Text.Trim();
        var list=all.Where(x=>(x.DoneAt!=null)==showDone&&(!unreadOnly||!IsRead(x))&&Matches(x,x.Name.Length>0||term.Length==0?x.Name:NameFor(data,x.Phone),term)).OrderBy(x=>showDone?DateTime.MaxValue-(x.DoneAt??x.CreatedAt):(x.DueAt??x.CreatedAt)-DateTime.MinValue).ToList();
        var listMode=view.Mode=="Lista";cards.ItemWidth=listMode?Math.Max(300,cards.ActualWidth-4):double.NaN;cards.Children.Clear();var index=0;foreach(var item in list)cards.Children.Add(Card(item,data,index++,listMode));
        var searching=term.Length>0;empty.ActionText=searching?"Limpar pesquisa":"";
        empty.Title=searching?"Nenhum lembrete encontrado":showDone?"Nenhum lembrete concluído ainda":unreadOnly?"Nenhum lembrete não lido":"Nenhum lembrete pendente";empty.Hint=searching?$"Tente outro nome, telefone ou texto, ou veja em {(showDone?"Pendentes":"Concluídos")}.":showDone?"Os verificados aparecem aqui.":"Adicione um telefone e o que precisa verificar.";
        empty.Visibility=list.Count==0?Visibility.Visible:Visibility.Collapsed;
    }

    // Pesquisa: título, nome, texto e telefone, sem diferenciar acentos. Número sozinho também procura no telefone, ignorando a formatação.
    public static bool Matches(Reminder item,string name,string term)
    {
        var key=ProductOperations.Normalize(term.Trim());if(key.Length==0)return true;
        if(new[]{item.Title,name,item.Description,item.Phone}.Any(x=>ProductOperations.Normalize(x).Contains(key)))return true;
        var digits=PhoneText.Digits(term);return digits.Length>0&&!key.Any(char.IsLetter)&&PhoneText.Digits(item.Phone).Contains(digits);
    }

    // UX 2.0 (pacote 3): cartão no padrão do protótipo (filete azul quando novo, concluído esmaecido e riscado) e ações com rótulo.
    // Em "Lista" o mesmo item ocupa a largura toda (telefone · texto · idade · ações). Mesmas ações e gravações de antes.
    Border Card(Reminder item,AppData data,int index,bool listMode=false)
    {
        var done=item.DoneAt!=null;var name=item.Name.Length>0?item.Name:NameFor(data,item.Phone);var unread=!done&&!IsRead(item);
        var card=new Border{Width=listMode?double.NaN:330,MinHeight=listMode?0:150,Margin=new Thickness(0,0,listMode?0:12,listMode?8:12),Padding=new Thickness(16,12,12,12),CornerRadius=new CornerRadius(listMode?10:14),BorderThickness=new Thickness(unread?4:1,1,1,1),Opacity=done?.62:1,ToolTip="Dois cliques para editar",Cursor=System.Windows.Input.Cursors.Hand};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,unread?"CInfo":"Line");
        var age=(DateTime.Now-item.CreatedAt).TotalDays;var late=!done&&item.DueAt is DateTime dueAt&&dueAt<DateTime.Now;var when=done?$"Verificado em {item.DoneAt:dd/MM HH:mm}":item.DueAt is DateTime due?(late?"Atrasado · ":"Avisar ")+ReminderDialog.When(due):age<1?$"Hoje, {item.CreatedAt:HH:mm}":age<2?"Ontem":$"Há {(int)age} dias";
        var who=new StackPanel();if(unread){var tag=new StatusTag{Tone="ready",Text="Novo",IconName="bell",Margin=new Thickness(0,0,0,4)};who.Children.Add(tag);}
        // Lembrete com título: título em destaque e cliente/telefone embaixo. Lembrete rápido: nome em destaque (telefone embaixo); sem nome, o telefone.
        var hasTitle=item.Title.Length>0;var hasPhone=PhoneText.Digits(item.Phone).Length>=8;
        who.Children.Add(new TextBlock{Text=hasTitle?item.Title:name.Length>0?name:PhoneText.Format(item.Phone),FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        var nameText=new TextBlock{Text=hasTitle?string.Join(" · ",new[]{name,hasPhone?PhoneText.Format(item.Phone):""}.Where(x=>x.Length>0)):name.Length>0?(hasPhone?PhoneText.Format(item.Phone):"Sem telefone"):"Cliente não cadastrado",FontSize=12.5,Margin=new Thickness(0,2,0,0)};nameText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");who.Children.Add(nameText);
        var whatsapp=new WhatsAppButton{Compact=true,Phone=item.Phone,OpenOnClick=false,VerticalAlignment=VerticalAlignment.Top,ToolTip="Abrir a conversa no WhatsApp Desktop"};if(!hasPhone)whatsapp.Visibility=Visibility.Collapsed;
        whatsapp.Click+=(_,_)=>Run(()=>WhatsAppActions.Open(this,item.Phone,""));
        var text=new TextBlock{Text=item.Description,TextWrapping=TextWrapping.Wrap,FontSize=14,TextDecorations=done?TextDecorations.Strikethrough:null};
        var whenText=new TextBlock{Text=when,ToolTip=when,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12,FontWeight=late||!done&&item.DueAt==null&&age>=3?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};whenText.SetResourceReference(TextBlock.ForegroundProperty,late?"Danger":!done&&item.DueAt==null&&age>=3?"Warning":"Muted");
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        Button TextAction(string label,string tip,Action action){var b=new Button{Content=label,Style=(Style)FindResource("SmallButton"),Margin=new Thickness(6,0,0,0),ToolTip=tip};b.Click+=(_,_)=>Run(action);actions.Children.Add(b);return b;}
        Button IconAction(string icon,string tip,Action action,bool danger=false){var b=new Button{Style=(Style)FindResource("IconButtonSmall"),Tag=icon,ToolTip=tip,Margin=new Thickness(4,0,0,0)};if(danger)b.SetResourceReference(ForegroundProperty,"Danger");b.Click+=(_,_)=>Run(action);actions.Children.Add(b);return b;}
        if(!done)TextAction(IsRead(item)?"Não lido":"Lido",IsRead(item)?"Marcar como não lido":"Marcar como lido",()=>Change(item.Id,"Leitura do lembrete alterada",r=>{var reader=Reader;if(r.ReadByUsers.ContainsKey(reader))r.ReadByUsers.Remove(reader);else r.ReadByUsers[reader]=DateTime.UtcNow;}));
        if(!done)TextAction("Concluir","Marcar como verificado",()=>Change(item.Id,"Lembrete concluído",r=>r.DoneAt=DateTime.Now));
        else TextAction("Reabrir","Voltar para pendentes",()=>Change(item.Id,"Lembrete reaberto",r=>r.DoneAt=null));
        IconAction("pencil","Editar",()=>Edit(item));
        IconAction("trash","Excluir",()=>{if(AlertWindow.Confirm(this,"Excluir lembrete",$"Excluir o lembrete \"{(item.Title.Length>0?item.Title:PhoneText.Format(item.Phone))}\"?",true))Change(item.Id,"Lembrete excluído",null);},true);
        var grid=new Grid();card.Child=grid;
        if(listMode)
        {
            foreach(var w in new[]{new GridLength(230),new GridLength(1,GridUnitType.Star),new GridLength(175),GridLength.Auto,GridLength.Auto})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=w});
            grid.Children.Add(who);text.Margin=new Thickness(12,0,12,0);text.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(text,1);grid.Children.Add(text);Grid.SetColumn(whenText,2);grid.Children.Add(whenText);
            whatsapp.Margin=new Thickness(0,0,6,0);whatsapp.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(whatsapp,3);grid.Children.Add(whatsapp);Grid.SetColumn(actions,4);grid.Children.Add(actions);
        }
        else
        {
            foreach(var h in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})grid.RowDefinitions.Add(new RowDefinition{Height=h});
            grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            grid.Children.Add(who);Grid.SetColumn(whatsapp,1);grid.Children.Add(whatsapp);
            text.Margin=new Thickness(0,10,0,10);Grid.SetRow(text,1);Grid.SetColumnSpan(text,2);grid.Children.Add(text);
            whenText.Margin=new Thickness(0,0,0,8);Grid.SetRow(whenText,2);Grid.SetColumnSpan(whenText,2);grid.Children.Add(whenText);actions.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(actions,3);Grid.SetColumnSpan(actions,2);grid.Children.Add(actions);
        }
        card.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2)Edit(item);};
        card.MouseEnter+=(_,_)=>{if(!unread)card.SetResourceReference(Border.BorderBrushProperty,"KindLembrete");};card.MouseLeave+=(_,_)=>card.SetResourceReference(Border.BorderBrushProperty,unread?"CInfo":"Line");
        return card;
    }
    // Alteração de um lembrete (null = excluir), sempre relida do banco e registrada na auditoria.
    async void Change(Guid id,string action,Action<Reminder>? change)
    {
        if(!IsEnabled)return;IsEnabled=false;try{if(store.IsRemote){var previous=await store.QueryRemoteAsync<Reminder>($"desktop/reminders/{id}");var draft=System.Text.Json.JsonSerializer.Deserialize<Reminder>(System.Text.Json.JsonSerializer.Serialize(previous))!;change?.Invoke(draft);await store.RemoteDesktopWriteAsync("desktop/reminders",new DesktopReminderCommand(Guid.NewGuid(),id,DataStore.DesktopRevision(previous),change==null?null:draft));}else {var unreadCount=await Task.Run(()=>store.Update(d=>{d.Reminders??=[];var r=d.Reminders.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Lembrete não encontrado. A lista foi atualizada.");
            if(change==null)d.Reminders.Remove(r);else change(r);AuditService.Record(d,App.CurrentUser,action,"Lembrete",r.Phone,"","","",$"{PhoneText.Format(r.Phone)} — {r.Description}");return 0;}));}
        Refresh();}catch(Exception ex){AlertWindow.Info(this,"Lembrete não alterado",ex.Message);}finally{IsEnabled=true;}
    }
    // Editar: mesma tela do Novo lembrete (lembrete rápido antigo abre com o texto no título).
    void Edit(Reminder item)
    {
        var dialog=new ReminderDialog(item){Owner=this};if(dialog.ShowDialog()!=true||dialog.Result is not Reminder edited)return;
        Run(()=>Change(item.Id,"Lembrete alterado",r=>{r.Title=edited.Title;r.Description=edited.Description;r.DueAt=edited.DueAt;r.Phone=edited.Phone;r.Name=edited.Name;}));
    }
}
