using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 4): barra superior (busca Ctrl+K, situação do caixa, "+ Novo", usuário), página Sistema (P7)
// e Pedidos Lista ⇄ Quadro. Cada opção chama exatamente o mesmo fluxo de antes, com as mesmas permissões.
public partial class MainWindow
{
    static readonly SystemWindow.Group[] SystemGroups=
    [
        new("Geral e arquivos","settings","Logotipo da gráfica, taxas de pagamento e pasta dos arquivos (PDFs, recibos, fotos)."),
        new("Orçamento","calculator","Parâmetros da calculadora de orçamentos."),
        new("Usuários e permissões","users","Quem acessa o ERP e o que cada um pode fazer."),
        new("Computadores e sessões","monitor","Outros computadores conectados, sessões abertas e pareamento."),
        new("Segurança e backups","shield-check","Auditoria das alterações, histórico e cópias de segurança."),
        new("Minha conta","user-round","Sua senha de acesso e sair do ERP."),
        new("Ferramentas","notebook-pen","Modelos de mensagem, anotações da equipe e catálogo visual."),
    ];
    static readonly RoutedEventArgs NoArgs=new();

    // Opções do Sistema (antes no menu do botão Sistema). Mesmas condições de permissão do menu antigo.
    List<AppCommand> SystemCommands()
    {
        ApplyPermissions(App.CurrentUser.Permissions);
        var list=new List<AppCommand>();
        void Add(string group,string title,string hint,string icon,string keys,Action run,string shortcut="")=>list.Add(new(group,title,hint,icon,keys,run,shortcut));
        if(Can("system.settings"))
        {
            Add("Geral e arquivos","Dados da gráfica e logotipo","Nome, CNPJ, endereço e logotipo dos PDFs; acesso à Área do Proprietário.","image","configurações geral logo logotipo proprietário empresa cnpj endereço dados da gráfica",()=>ShowSettings(0));
            Add("Geral e arquivos","Taxas de pagamento","Percentual estimado de cada forma de pagamento (cartão, PIX…).","percent","configurações taxa cartão crédito débito pix",()=>ShowSettings(1));
            Add("Geral e arquivos","Arquivos e caminhos","Pasta compartilhada dos PDFs, recibos e fotos; testar acesso.","folder","configurações pasta caminho rede arquivos pdf",()=>ShowSettings(2));
            Add("Geral e arquivos","Prazos, feriados e regras","Opções de prazo, feriados de São Paulo, prioridade, sinal, faixas de devedores, validade do orçamento e cópias internas.","calendar-days","prazo feriado sábado prioridade sinal devedores faixa orçamento validade condições regras backup cópias",()=>ShowSettings(4));
            Add("Orçamento","Parâmetros de orçamento","Regras da calculadora de orçamentos (antes em Configurações › Geral).","calculator","orçamento calculadora parâmetros preço m2",ShowBudgetSettings);
        }
        if(Can("system.users"))Add("Usuários e permissões","Usuários e permissões","Criar, editar, desativar, redefinir senha, perfis base e permissões.","users","usuário login perfil permissão acesso senha",()=>new UsersWindow(store){Owner=this}.ShowDialog());
        if((Can("system.users"))&&store.AuthenticatedUser?.Role=="Administrador")Add("Computadores e sessões","Sessões e dispositivos","Ver e encerrar sessões; revogar computadores e celulares pareados.","monitor","sessões dispositivos celular computador revogar",()=>new DevicesWindow(store){Owner=this}.ShowDialog());
        Add("Computadores e sessões","Conectar outro computador","Endereços e código para ligar outro PC a este.","network","conexão pareamento outro pc rede tailscale",()=>Connection_Click(this,NoArgs));
        if(Can("system.audit"))Add("Segurança e backups","Auditoria","Quem alterou o quê e quando, com antes e depois.","shield-check","auditoria histórico alterações log",()=>new AuditWindow(store){Owner=this}.ShowDialog());
        if(Can("system.backups"))Add("Segurança e backups","Histórico e backups","Últimas alterações e cópias de segurança; restaurar.","hard-drive","backup cópia restaurar histórico",()=>History_Click(this,NoArgs));
        if(Can("system.backups"))Add("Segurança e backups","Backup programado","Cópia automática para HD externo, pendrive ou nuvem: pasta, horário, dias e quantas guardar.","clock","backup automático agendado programado horário cópia externa pendrive onedrive",()=>ShowSettings(3));
        Add("Minha conta","Trocar senha de acesso","Vale para todos os computadores.","key-round","senha trocar alterar",()=>ChangePassword_Click(this,NoArgs));
        Add("Minha conta","Sair","Encerra a sessão neste computador.","log-out","sair logout encerrar",Logout);
        if(Can("system.settings"))Add("Ferramentas","Modelos de mensagem","Textos prontos para WhatsApp e e-mail usados nos pedidos.","message-circle","modelos mensagem whatsapp e-mail texto cobrança pronto",()=>new CommunicationTemplatesWindow(store){Owner=this}.ShowDialog());
        Add("Ferramentas","Caderno de melhorias","Anotações da equipe sobre o que melhorar no ERP.","notebook-pen","caderno melhorias anotações ideias",()=>Notes_Click(this,NoArgs));
        if(store.AuthenticatedUser?.Role=="Administrador")Add("Ferramentas","Catálogo de componentes","Vitrine interna dos botões, campos e diálogos do ERP.","layout-grid","catálogo componentes visual",()=>new ComponentCatalogWindow{Owner=this}.ShowDialog());
        return list;
    }

    // Tudo o que a busca Ctrl+K encontra: telas do menu (só as visíveis), ações "Novo" e as opções do Sistema.
    List<AppCommand> AllCommands()
    {
        var list=new List<AppCommand>();
        void Page(Button nav,string title,string hint,string icon,string keys,Action run,string shortcut=""){if(nav.Visibility==Visibility.Visible)list.Add(new("Telas",title,hint,icon,keys,run,shortcut));}
        list.Add(new("Ações","Novo lançamento","Venda paga na hora; entra no caixa.","wallet","novo lançamento venda caixa",()=>Add_Click(this,NoArgs),"L"));
        list.Add(new("Ações","Novo pedido","Produção com prazo, sinal e saldo.","clipboard-list","novo pedido produção",NewOrder,"N"));
        list.Add(new("Ajuda","Atalhos do teclado","Lista das teclas de atalho do ERP.","info","atalhos teclado teclas ajuda",ShowShortcuts,"F1"));
        if(Can("view.products")||!store.IsRemote)list.Add(new("Ações","Novo produto","Cadastro de produto ou serviço.","package","novo produto cadastro serviço",NewProduct));
        if(Can("view.purchases")||!store.IsRemote)list.Add(new("Ações","Nova compra","Material que precisa ser comprado.","shopping-cart","nova compra necessidade material",()=>{PurchaseNeeds_Click(this,NoArgs);(pages.Current as PurchaseNeedsWindow)?.StartNew();}));
        if(Can("view.reminders")||!store.IsRemote)list.Add(new("Ações","Novo lembrete","Verificar depois na conversa do WhatsApp.","bell","novo lembrete whatsapp",()=>{Reminders_Click(this,NoArgs);(pages.Current as RemindersWindow)?.StartNew();}));
        list.Add(new("Ações","Recibo avulso","Recibo em PDF com CNPJ/CPF do cliente.","receipt","recibo avulso pdf",()=>Receipt_Click(this,NoArgs)));
        list.Add(new("Ações",CloseButton.Visibility==Visibility.Visible?"Fechar caixa":"Abrir caixa",CloseButton.Visibility==Visibility.Visible?"Conferir o dinheiro e fechar o caixa de hoje.":"Informar o dinheiro inicial e abrir o caixa.","wallet","caixa abrir fechar fechamento",CashChip_Toggle));
        list.Add(new("Ações","Sangria do caixa","Retirada de dinheiro do caixa.","banknote","sangria retirada dinheiro",()=>Withdrawal_Click(this,NoArgs)));
        list.Add(new("Ações","Ajuste de caixa","Corrige o dinheiro esperado sem contar como venda.","wallet","ajuste acerto correcao caixa gaveta dinheiro esperado",()=>Adjustment_Click(this,NoArgs)));
        if(Can("view.cash")||!store.IsRemote)list.Add(new("Ações","Conciliação Pix","Conferir o extrato do banco (OFX) com os PIX lançados.","banknote","conciliação conciliar pix extrato banco ofx",()=>ReconciliationWindow.Open(this,store)));
        if(Can("view.orders")||!store.IsRemote)list.Add(new("Ações","Avisar retiradas","WhatsApp de quem tem pedido pronto há alguns dias.","message-circle","avisar retirada pronto lembrete whatsapp",async()=>await WhatsAppActions.PickupQueueAsync(this,store)));
        Page(NavHome,"Início","Caixa de hoje e próximos pedidos.","house","início painel dashboard home",()=>Home_Click(this,NoArgs));
        Page(NavOrders,"Pedidos e produção","Lista ou quadro dos pedidos.","clipboard-list","pedidos produção quadro kanban",()=>OpenOrders(),"F4");
        Page(NavBudget,"Orçamentos","Calculadora prática e prazos de produção.","file-text","orçamento calculadora prazos",()=>Budget_Click(this,NoArgs));
        if(Can("view.prospects")||!store.IsRemote)Page(NavBudget,"Orçamentos em acompanhamento","Orçamentos enviados: cobrar o retorno e marcar se virou pedido.","clipboard-list","orçamento acompanhamento retorno possíveis pedidos cobrar",OpenProspects);
        Page(NavPurchases,"Compras","O que precisa ser comprado.","shopping-cart","compras material",()=>PurchaseNeeds_Click(this,NoArgs));
        Page(NavSuppliers,"Fornecedores","Contatos, prazos e preços de quem vende os materiais.","truck","fornecedores fornecedor loja comprar preço",()=>Suppliers_Click(this,NoArgs));
        Page(NavProducts,"Produtos","Preços, faixas e custos.","package","produtos preço custo serviços",()=>Products_Click(this,NoArgs));
        Page(NavCustomers,"Clientes","Ficha com pedidos e valores pendentes.","users","clientes ficha telefone",()=>Customers_Click(this,NoArgs));
        Page(NavReminders,"Lembretes","Verificar depois no WhatsApp.","message-circle","lembretes whatsapp",()=>Reminders_Click(this,NoArgs));
        Page(NavDebtors,"Devedores","Quem está devendo e cobranças.","wallet","devedores cobrança dívida saldo",()=>Debtors_Click(this,NoArgs));
        Page(NavClosed,"Fechamentos","Caixas anteriores.","archive","fechamentos caixas anteriores",()=>ClosedSessions_Click(this,NoArgs));
        Page(NavSearch,"Pesquisar lançamentos","Encontrar vendas e recebimentos.","search","pesquisar lançamentos vendas",()=>Search_Click(this,NoArgs),"F3");
        Page(NavReports,"Relatórios financeiros","Resultados, formas de pagamento e custos.","chart-no-axes-combined","relatórios financeiro resultado",()=>Reports_Click(this,NoArgs));
        list.Add(new("Telas","Sistema","Configurações, usuários, segurança e aparência.","settings","sistema configurações",()=>ShowSystem()));
        foreach(var c in SystemCommands())list.Add(c with {Group="Sistema · "+c.Group});
        list.Add(new("Sistema · Aparência","Aparência","Tema claro/escuro, cores, tamanho do texto e movimento.","palette","tema claro escuro cores viva sóbria tamanho texto fonte movimento animação",()=>ShowSystem("Aparência")));
        list.Add(new("Sistema · Aparência",ThemeManager.IsDark?"Usar tema claro":"Usar tema escuro","Troca o tema agora.","sun","tema claro escuro",()=>ThemeManager.Set(ThemeManager.IsDark?AppTheme.Claro:AppTheme.Escuro,ThemeManager.TextScale)));
        return list;
    }

    void CommandPalette_Click(object s,RoutedEventArgs e)=>OpenCommandPalette();
    void OpenCommandPalette(){if(OwnedWindows.OfType<Window>().Any(w=>w.IsVisible&&w!=pages.Current))return;CommandPalette.Open(this,AllCommands(),CommandButton);}

    void System_Click(object s,RoutedEventArgs e)=>ShowSystem();
    void ShowSystem(string? group=null)=>Navigate(NavSystem,()=>new SystemWindow(SystemCommands(),SystemGroups,group));

    async void ShowSettings(int tab){try{new SettingsWindow(store,await store.BusinessContextAsync("settings"),tab){Owner=this}.ShowDialog();RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Configurações",ex.Message);}}
    async void ShowBudgetSettings(){try{new BudgetSettingsWindow(store,await store.BusinessContextAsync("budget")){Owner=this}.ShowDialog();}catch(Exception ex){AlertWindow.Info(this,"Parâmetros de orçamento",ex.Message);}}
    void Logout(){try{store.Logout();App.AuthenticatedStore=null;Close();}catch(Exception ex){AlertWindow.Info(this,"Sair",ex.Message);}}

    // Situação do caixa na barra superior: clique abre ou fecha o caixa (mesmos fluxos dos botões do menu).
    void UpdateCashChip(CashSession? open)
    {
        CashChipText.Text=open!=null?$"Caixa aberto · {open.ExpectedCash:C2}":"Caixa fechado";
        CashChipDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,open!=null?"COk":"Muted");
        CashChip.ToolTip=open!=null?$"Aberto desde {(open.OpenedAt.Date==DateTime.Today?open.OpenedAt.ToString("HH:mm"):open.OpenedAt.ToString("dd/MM HH:mm"))} · dinheiro esperado na gaveta: {open.ExpectedCash:C2}. Clique para conferir e fechar o caixa.":"Clique para abrir o caixa";
    }
    void CashChip_Click(object s,RoutedEventArgs e)=>CashChip_Toggle();
    // Consulta a situação real do caixa antes de agir (outro computador pode ter aberto/fechado enquanto você estava em outra tela).
    async void CashChip_Toggle(){try{var open=Current(await store.BusinessContextAsync("cash"));UpdateCashChip(open);if(open!=null)Close_Click(this,NoArgs);else Open_Click(this,NoArgs);}catch(Exception ex){AlertWindow.Info(this,"Caixa",ex.Message);}}

    // Menu do usuário: tema, tamanho do texto, senha e sair (o "menu curto" do mapa de telas).
    void UserChip_Click(object s,RoutedEventArgs e)
    {
        var menu=new ContextMenu{PlacementTarget=UserChip,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        var head=new MenuItem{Header=$"{App.CurrentUser.UserName} · {App.CurrentUser.Role}",IsEnabled=false};menu.Items.Add(head);menu.Items.Add(new Separator());
        var theme=new MenuItem{Header="Tema"};
        foreach(var (option,label) in new[]{(AppTheme.Automatico,"Automático (igual ao Windows)"),(AppTheme.Claro,"Claro"),(AppTheme.Escuro,"Escuro")}){var m=new MenuItem{Header=label,IsCheckable=true,IsChecked=ThemeManager.Theme==option};m.Click+=(_,_)=>ThemeManager.Set(option,ThemeManager.TextScale);theme.Items.Add(m);}
        var size=new MenuItem{Header="Tamanho do texto"};
        foreach(var (scale,label) in new[]{(1.0,"Normal"),(1.12,"Grande"),(1.25,"Muito grande")}){var m=new MenuItem{Header=label,IsCheckable=true,IsChecked=Math.Abs(ThemeManager.TextScale-scale)<0.01};m.Click+=(_,_)=>ThemeManager.Set(ThemeManager.Theme,scale);size.Items.Add(m);}
        menu.Items.Add(theme);menu.Items.Add(size);
        void Item(string text,Action action){var m=new MenuItem{Header=text};m.Click+=(_,_)=>action();menu.Items.Add(m);}
        Item("Aparência e configurações…",()=>ShowSystem("Aparência"));
        Item("Trocar senha de acesso",()=>ChangePassword_Click(this,NoArgs));
        menu.Items.Add(new Separator());
        Item("Sair",Logout);
        menu.IsOpen=true;
    }
    void UpdateUserChip()
    {
        var name=App.CurrentUser.UserName.Trim();
        UserInitials.Text=string.Concat(name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x=>char.ToUpperInvariant(x[0])));
        if(UserInitials.Text.Length==0)UserInitials.Text="?";
        UserNameText.Text=name.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
        UserChip.ToolTip=$"{name} · {App.CurrentUser.Role}";
    }

    // Pedidos: a lista e o quadro são duas vistas da mesma área; a última escolha fica gravada por usuário.
    internal void ShowOrdersBoard()=>Dispatcher.BeginInvoke(()=>Navigate(NavOrders,()=>new OrderBoardWindow(store)));
    internal void ShowOrdersList()=>Dispatcher.BeginInvoke(()=>Navigate(NavOrders,()=>new ScheduledOrdersWindow(store,null)));
}
