using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace GraficaCaixa.Wpf;

public static class UnsavedChanges
{
    sealed class Guard(Action save,Func<string> read){public Action Save=save;public Func<string> Read=read;public string? Original;public Action? BeforePrompt;public Action? Discard;public Action? Resume;public bool BypassClose;public bool PromptPending;}
    static readonly ConditionalWeakTable<Window,Guard> guards=new();
    public static void Attach(Window window,Action save,Func<string>? read=null,Action? beforePrompt=null,Action? discard=null,Action? resume=null)
    {
        var guard=new Guard(save,read??(()=>Snapshot(window))){BeforePrompt=beforePrompt,Discard=discard,Resume=resume};
        guards.Add(window,guard);
        window.Loaded+=(_,_)=>guard.Original??=guard.Read();
        window.Closing+=(_,e)=>
        {
            if(guard.BypassClose||window.DialogResult is not null||!IsDirty(window))return;
            e.Cancel=true;
            if(guard.PromptPending)return;
            guard.PromptPending=true;
            window.Dispatcher.BeginInvoke(new Action(()=>
            {
                try{if(window.IsVisible)Escape(window);}
                finally{guard.PromptPending=false;}
            }));
        };
    }
    public static bool HasOpenEscapeElement(DependencyObject root)
    {
        var visited=new HashSet<DependencyObject>();
        bool Walk(DependencyObject item)
        {
            if(!visited.Add(item))return false;
            if(item is ComboBox {IsDropDownOpen:true} or ContextMenu {IsOpen:true} or Popup {IsOpen:true} or MenuItem {IsSubmenuOpen:true})return true;
            if(item is FrameworkElement {ContextMenu:{IsOpen:true}})return true;
            foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())if(Walk(child))return true;
            if(item is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(item);i++)if(Walk(System.Windows.Media.VisualTreeHelper.GetChild(item,i)))return true;
            return false;
        }
        return Walk(root);
    }
    public static bool IsDirty(Window window)=>guards.TryGetValue(window,out var guard)&&guard.Original!=null&&guard.Original!=guard.Read();
    public static void Escape(Window window)
    {
        if(!IsDirty(window)){window.Close();return;}
        var guard=guards.GetValue(window,_=>throw new InvalidOperationException());guard.BeforePrompt?.Invoke();
        var decision=new UnsavedChangesWindow{Owner=window};
        decision.ShowDialog();
        if(decision.Choice=="Salvar")guard.Save();
        else if(decision.Choice=="Descartar"){guard.Discard?.Invoke();guard.BypassClose=true;window.Close();}
        else guard.Resume?.Invoke();
    }
    // Atalhos que não são dados do formulário (ex.: botões "Mais vendidos" carregados depois de abrir) ficam fora da comparação.
    public static readonly DependencyProperty IgnoreProperty=DependencyProperty.RegisterAttached("Ignore",typeof(bool),typeof(UnsavedChanges),new PropertyMetadata(false));
    public static void SetIgnore(DependencyObject element,bool value)=>element.SetValue(IgnoreProperty,value);
    public static bool GetIgnore(DependencyObject element)=>(bool)element.GetValue(IgnoreProperty);
    public static string Snapshot(DependencyObject root)
    {
        var values=new List<string>();
        void Walk(DependencyObject item)
        {
            if(GetIgnore(item))return;
            if(item is TextBox text){if(!text.IsReadOnly)values.Add("T:"+text.Text);return;}
            if(item is DatePicker date){values.Add("D:"+date.SelectedDate?.ToString("O"));return;}
            if(item is ComboBox combo){values.Add("C:"+combo.SelectedIndex+":"+combo.Text);return;}
            if(item is ToggleButton toggle){values.Add("B:"+toggle.IsChecked);return;}
            foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())Walk(child);
        }
        Walk(root);return JsonSerializer.Serialize(values);
    }
}
public sealed class UnsavedChangesWindow:Window
{
    public string Choice{get;private set;}="Continuar editando";
    public UnsavedChangesWindow()
    {
        Title="Alterações não salvas";Width=540;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(26)};Content=panel;
        panel.Children.Add(new TextBlock{Text="Você tem alterações não salvas",Style=(Style)FindResource("SectionTitle")});
        panel.Children.Add(new TextBlock{Text="O que deseja fazer antes de sair?",Margin=new Thickness(0,10,0,22)});
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        foreach(var label in new[]{"Continuar editando","Descartar","Salvar"})
        {
            var b=new Button{Content=label,Style=(Style)FindResource(label=="Salvar"?"PrimaryButton":"SoftButton"),Margin=new Thickness(6,0,0,0)};
            b.Click+=(_,_)=>{Choice=label;DialogResult=true;};buttons.Children.Add(b);
        }
    }
}
