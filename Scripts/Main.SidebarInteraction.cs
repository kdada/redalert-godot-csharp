using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    const float FundsHeaderHeight=19.5f;
    int productPage;
    int productPageCapacity=2;
    int ProductPageCount=>Math.Max(1,(productButtons.Count+productPageCapacity-1)/productPageCapacity);

    void ToggleMaintenance(CommandType mode)
    {
        if(battlefield is null || IsSpectating || MatchMenuOpen)return;
        battlefield.SetMaintenanceMode(battlefield.MaintenanceMode==mode?null:mode);
        Notify(battlefield.MaintenanceMode is null?"已取消维修 / 出售模式":mode==CommandType.Repair?"维修模式：左键点击己方建筑；再次点击可停止维修，右键 / Esc 取消":"出售模式：左键点击己方建筑；右键 / Esc 取消");
    }

    public void RefreshMaintenanceButtons()
    {
        var canAct=Battle is not null && LocalPlayer>=0 && !IsSpectating && !MatchMenuOpen;
        var mode=battlefield?.MaintenanceMode;
        if(repairButton is not null){repairButton.Disabled=!canAct;repairButton.SetPressedNoSignal(mode==CommandType.Repair);repairButton.Modulate=canAct?Colors.White:new Color(.48f,.48f,.48f);}
        if(sellButton is not null){sellButton.Disabled=!canAct;sellButton.SetPressedNoSignal(mode==CommandType.Sell);sellButton.Modulate=canAct?Colors.White:new Color(.48f,.48f,.48f);}
    }

    void RefreshProductPage()
    {
        if(products is null)return;
        productPage=Math.Clamp(productPage,0,ProductPageCount-1);
        var first=productPage*productPageCapacity;
        for(var i=0;i<productButtons.Count;i++)
        {
            var visible=i>=first && i<first+productPageCapacity;
            if(productButtons[i].Button.Visible!=visible)productButtons[i].Button.Visible=visible;
        }
        // A page is a fixed set of complete rows, including a short last page.
        // No pixel-scroll offset can hide its first row or expose the next page.
        if(productionScroll is not null)productionScroll.ScrollVertical=0;
        var multiple=ProductPageCount>1;
        if(scrollUp is not null){scrollUp.Visible=multiple;scrollUp.Disabled=productPage==0;scrollUp.TooltipText=$"上一页 · {productPage+1} / {ProductPageCount}";scrollUp.QueueRedraw();}
        if(scrollDown is not null){scrollDown.Visible=multiple;scrollDown.Disabled=productPage>=ProductPageCount-1;scrollDown.TooltipText=$"下一页 · {productPage+1} / {ProductPageCount}";scrollDown.QueueRedraw();}
    }

    void HandleCatalogWheel(InputEvent input)
    {
        if(input is not InputEventMouseButton{Pressed:true} mouse || mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))return;
        ScrollProducts(mouse.ButtonIndex==MouseButton.WheelUp?-1:1);productionScroll?.AcceptEvent();
    }

    public Godot.Collections.Dictionary GetSidebarInteractionAudit()=>new()
    {
        {"maintenanceMode",battlefield?.MaintenanceMode?.ToString()??"None"},
        {"repairDisabled",repairButton?.Disabled??true},{"sellDisabled",sellButton?.Disabled??true},
        {"repairPressed",repairButton?.ButtonPressed??false},{"sellPressed",sellButton?.ButtonPressed??false},
        {"productTab",productTab},{"pageIndex",productPage},{"pageNumber",productPage+1},
        {"pageCount",ProductPageCount},{"pageCapacity",productPageCapacity},{"catalogCount",productButtons.Count},
        {"visibleCount",productButtons.Count(p=>p.Button.Visible)},{"firstCatalogIndex",productPage*productPageCapacity},
        {"pageUpVisible",scrollUp?.Visible??false},{"pageDownVisible",scrollDown?.Visible??false},
        {"pageUpDisabled",scrollUp?.Disabled??true},{"pageDownDisabled",scrollDown?.Disabled??true},
        {"fundsAtTop",creditsLabel?.Position.Y<FundsHeaderHeight},
        {"funds",creditsLabel?.Text??""},{"scrollOffset",productionScroll?.ScrollVertical??0}
    };
}
