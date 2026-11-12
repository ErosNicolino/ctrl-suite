using System.Windows;
namespace GraficaCaixa.Wpf;

// Janela nunca maior que a tela (B7): várias janelas têm tamanho fixo (Auditoria 1100 × 700, Editar pedido 1264 × 912) e em telas
// de 1024 × 768 ficavam cortadas. No primeiro desenho, largura/altura (e os mínimos) são limitados à área útil da tela; se a
// janela estiver na tela, ela é trazida para dentro. Janelas maximizadas e as que já cabem não mudam.
public static class WindowFit
{
    public static void Register()=>EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler((sender,_)=>Fit((Window)sender)));
    static void Fit(Window window)
    {
        if(window.WindowState!=WindowState.Normal)return;
        var area=SystemParameters.WorkArea;var maxWidth=Math.Max(320,area.Width-16);var maxHeight=Math.Max(240,area.Height-16);
        // O Windows pode informar o tamanho já reduzido enquanto o conteúdo usa o pedido (Width/Height): vale o maior dos dois.
        var width=double.IsNaN(window.Width)?window.ActualWidth:Math.Max(window.Width,window.ActualWidth);var height=double.IsNaN(window.Height)?window.ActualHeight:Math.Max(window.Height,window.ActualHeight);
        if(width<=maxWidth+1&&height<=maxHeight+1)return;
        if(window.MinWidth>maxWidth)window.MinWidth=maxWidth;if(window.MinHeight>maxHeight)window.MinHeight=maxHeight;
        if(width>maxWidth+1)window.Width=maxWidth;if(height>maxHeight+1)window.Height=maxHeight;
        // Só reposiciona quem está (ao menos em parte) na tela; janelas fora da tela de propósito ficam onde estão.
        var visible=window.Left<area.Right&&window.Left+window.ActualWidth>area.Left&&window.Top<area.Bottom&&window.Top+window.ActualHeight>area.Top;
        if(!visible)return;
        window.Left=Math.Max(area.Left+8,Math.Min(window.Left,area.Right-window.Width-8));window.Top=Math.Max(area.Top+8,Math.Min(window.Top,area.Bottom-window.Height-8));
    }
}
