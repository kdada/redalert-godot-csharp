using Godot;

namespace RedAlert;

public partial class Main
{
    Window? inputWindow;
    BattlePointerLayer? battlePointerLayer;
    bool battleWindowInput,softwareBattlePointer,softwareMenuPointer,forwardingBattlePointer,pointerInClient;
    ClassicCursorLibrary? classicCursorLibrary;
    DesktopPointerMotion? desktopPointerMotion;
    Vector2 softwarePointer;
    bool pointerChangedAccumulation,previousInputAccumulation;
    void RestorePointerAccumulation()
    {
        if(!pointerChangedAccumulation)return;Input.UseAccumulatedInput=previousInputAccumulation;pointerChangedAccumulation=false;
    }
    public bool OriginalGamePointerVisible=>softwareBattlePointer || softwareMenuPointer;
    public string OriginalGamePointerKey=>OriginalGamePointerKeyAt(BattlePointerPosition());
    public bool IsBattleScrollPointer(Vector2 point)=>WantsBattleMouseConfined && clientPreferences.EdgeScrolling && !ChatTyping
        && minimap?.DraggingViewport!=true && BattleEdgeDirection(point)!=Vector2.Zero;
    public string OriginalGamePointerKeyAt(Vector2 point)
    {
        if(!WantsBattleMouseConfined || battlefield is null)return "arrow";
        // Outer client edges have priority, including the far side of the sidebar.
        if(IsBattleScrollPointer(point))
        {
            var direction=BattleEdgeDirection(point);
            var suffix=direction.Y<0?(direction.X<0?"nw":direction.X>0?"ne":"n")
                :direction.Y>0?(direction.X<0?"sw":direction.X>0?"se":"s"):(direction.X<0?"w":"e");
            return "scroll_"+suffix+(battlefield.CanScrollScreen(direction)?"":"_blocked");
        }
        return battlefield.OriginalActionCursor(point);
    }
    public void DrawOriginalGamePointer(CanvasItem canvas)=>classicCursorLibrary?.Draw(canvas,BattlePointerPosition(),OriginalGamePointerKey);
    public bool SoftwareBattlePointerActive=>softwareBattlePointer;
    public Vector2 BattlePointerPosition()=>softwareBattlePointer?softwarePointer:GetViewport().GetMousePosition();
    bool NeedsSoftwareBattlePointer=>!headless && !DisplayServer.HasFeature(DisplayServer.Feature.MouseWarp);

    void InitWindowInput()
    {
        if(inputWindow is not null)return;
        inputWindow=GetWindow();inputWindow.Unresizable=true;
        inputWindow.FocusEntered+=WindowInputFocusEntered;inputWindow.FocusExited+=WindowInputFocusExited;
        inputWindow.MouseEntered+=WindowPointerEntered;inputWindow.MouseExited+=WindowPointerExited;
        if(!headless)
        {
            classicCursorLibrary=new ClassicCursorLibrary();desktopPointerMotion=new DesktopPointerMotion(clientPreferences.PointerSpeed);
            pointerInClient=GetViewport().GetVisibleRect().HasPoint(GetViewport().GetMousePosition());
            battlePointerLayer=new BattlePointerLayer{Controller=this,Name="BattlePointer"};AddChild(battlePointerLayer);
        }
        UpdateWindowInput();
    }
    bool WantsBattleMouseConfined=>!headless && !ended && battleWindowInput && Battle is not null
        && battlefield is not null && battlefield.IsInsideTree() && battlefield.IsVisibleInTree()
        && inputWindow is not null && inputWindow.HasFocus() && !MatchMenuOpen
        && battleOptions?.Visible!=true && preparationOverlay is null;
    void SetBattleWindowInput(bool active){battleWindowInput=active;UpdateWindowInput();}
    void WindowInputFocusEntered()=>UpdateWindowInput();
    void WindowInputFocusExited()=>ReleaseWindowMouse();
    void WindowPointerEntered(){pointerInClient=true;UpdateWindowInput();}
    void WindowPointerExited(){pointerInClient=false;if(!softwareBattlePointer)ReleaseWindowMouse();}
    void ReleaseWindowMouse()
    {
        softwareBattlePointer=false;softwareMenuPointer=false;RestorePointerAccumulation();
        if(battlePointerLayer is not null)battlePointerLayer.Visible=false;
        if(!headless && Input.MouseMode!=Input.MouseModeEnum.Visible)Input.MouseMode=Input.MouseModeEnum.Visible;
    }
    Rect2 BattleContentRect()=>GetGlobalRect().Intersection(GetViewport().GetVisibleRect());
    Vector2 BattlePointerInset()
    {
        var content=BattleContentRect();var pixels=inputWindow?.Size??new Vector2I((int)content.Size.X,(int)content.Size.Y);
        return new Vector2(16*Math.Max(1,content.Size.X)/Math.Max(1,pixels.X),16*Math.Max(1,content.Size.Y)/Math.Max(1,pixels.Y));
    }
    Rect2 BattlePointerRect()
    {
        var content=BattleContentRect();var inset=BattlePointerInset();
        inset=new Vector2(Math.Min(inset.X,content.Size.X*.1f),Math.Min(inset.Y,content.Size.Y*.1f));
        return new Rect2(content.Position+inset,new Vector2(Math.Max(1,content.Size.X-inset.X*2),Math.Max(1,content.Size.Y-inset.Y*2)));
    }
    Vector2 ClampBattlePointer(Vector2 point)
    {
        var bounds=BattlePointerRect();
        return new Vector2(Math.Clamp(point.X,bounds.Position.X,bounds.End.X-.01f),Math.Clamp(point.Y,bounds.Position.Y,bounds.End.Y-.01f));
    }
    // The OpenHarmony .11 backend advertises no MouseWarp and its native warp
    // and window flag setters are empty. Captured mode has an actual stationary
    // OS cursor lock. Keep that cursor hidden and route a bounded visible cursor
    // through the normal Godot GUI instead of pretending WarpMouse succeeded.
    public void RouteBattlePointer(InputEvent input)
    {
        if(!softwareBattlePointer || forwardingBattlePointer || !WantsBattleMouseConfined || input is not InputEventMouse mouse)return;
        var previous=softwarePointer;
        if(mouse is InputEventMouseMotion motion)
        {
            // .11/API26 captured ScreenRelative contains hardware counts. Match
            // the desktop's own acceleration curve in physical screen pixels,
            // then convert to the stretched viewport once. Injected local events
            // without native ScreenRelative already carry their logical delta.
            var delta=motion.Relative;
            if(!motion.ScreenRelative.IsZeroApprox() && desktopPointerMotion is not null)
                delta=GetViewport().GetFinalTransform().AffineInverse().BasisXform(desktopPointerMotion.ToScreenPixels(motion.ScreenRelative))*clientPreferences.PointerSensitivity;
            softwarePointer=ClampBattlePointer(softwarePointer+delta);
        }
        var forwarded=(InputEventMouse)mouse.Duplicate();forwarded.Position=softwarePointer;forwarded.GlobalPosition=softwarePointer;
        forwarded.WindowId=GetWindow().GetWindowId();
        if(forwarded is InputEventMouseMotion moved)moved.Relative=softwarePointer-previous;
        forwardingBattlePointer=true;
        try{GetViewport().PushInput(forwarded,true);}
        finally{forwardingBattlePointer=false;GetViewport().SetInputAsHandled();}
    }
    void ConstrainBattlePointer(InputEvent input)
    {
        if(softwareBattlePointer || !WantsBattleMouseConfined || input is not InputEventMouse mouse)return;
        var clipped=ClampBattlePointer(mouse.Position);if(clipped.DistanceSquaredTo(mouse.Position)<.001f)return;
        GetViewport().WarpMouse(clipped);mouse.Position=clipped;mouse.GlobalPosition=clipped;
        if(mouse is InputEventMouseMotion motion)motion.Relative=Vector2.Zero;
    }
    void UpdateWindowInput()
    {
        if(inputWindow is null || !GodotObject.IsInstanceValid(inputWindow))return;
        if(headless)return;
        if(!WantsBattleMouseConfined)
        {
            softwareBattlePointer=false;RestorePointerAccumulation();
            softwareMenuPointer=NeedsSoftwareBattlePointer && inputWindow.HasFocus() && pointerInClient;
            var menuMode=softwareMenuPointer?Input.MouseModeEnum.Hidden:Input.MouseModeEnum.Visible;
            if(Input.MouseMode!=menuMode)Input.MouseMode=menuMode;
            if(battlePointerLayer is not null)battlePointerLayer.Visible=softwareMenuPointer;
            return;
        }
        softwareMenuPointer=false;
        if(NeedsSoftwareBattlePointer)
        {
            if(!softwareBattlePointer)
            {
                softwarePointer=ClampBattlePointer(GetViewport().GetMousePosition());desktopPointerMotion?.RefreshNativeSettings();
                previousInputAccumulation=Input.UseAccumulatedInput;Input.UseAccumulatedInput=false;pointerChangedAccumulation=true;
            }
            softwareBattlePointer=true;softwarePointer=ClampBattlePointer(softwarePointer);
            if(battlePointerLayer is not null)
            {
                battlePointerLayer.Visible=true;
                if(battlePointerLayer.GetIndex()!=GetChildCount()-1)MoveChild(battlePointerLayer,GetChildCount()-1);
            }
            if(Input.MouseMode!=Input.MouseModeEnum.Captured)Input.MouseMode=Input.MouseModeEnum.Captured;
        }
        else
        {
            softwareBattlePointer=false;
            if(!inputWindow.Unresizable)inputWindow.Unresizable=true;
            if(Input.MouseMode!=Input.MouseModeEnum.Confined)Input.MouseMode=Input.MouseModeEnum.Confined;
            var pointer=GetViewport().GetMousePosition();var clipped=ClampBattlePointer(pointer);
            if(pointer.DistanceSquaredTo(clipped)>.001f)GetViewport().WarpMouse(clipped);
        }
    }
    Vector2 BattleEdgeDirection(Vector2 pointer)
    {
        if(battlefield is null)return Vector2.Zero;var bounds=BattleContentRect();
        if(!bounds.HasPoint(pointer))return Vector2.Zero;
        var inset=BattlePointerInset();var margin=new Vector2(Math.Max(clientPreferences.EdgeScrollMargin,inset.X+4),Math.Max(clientPreferences.EdgeScrollMargin,inset.Y+4));
        var direction=Vector2.Zero;
        if(pointer.X<bounds.Position.X+margin.X)direction.X=-1;else if(pointer.X>=bounds.End.X-margin.X)direction.X=1;
        if(pointer.Y<bounds.Position.Y+margin.Y)direction.Y=-1;else if(pointer.Y>=bounds.End.Y-margin.Y)direction.Y=1;
        return direction;
    }
    void StopWindowInput()
    {
        battleWindowInput=false;ReleaseWindowMouse();
        if(inputWindow is not null && GodotObject.IsInstanceValid(inputWindow))
        {inputWindow.FocusEntered-=WindowInputFocusEntered;inputWindow.FocusExited-=WindowInputFocusExited;inputWindow.MouseEntered-=WindowPointerEntered;inputWindow.MouseExited-=WindowPointerExited;}
        inputWindow=null;classicCursorLibrary?.Dispose();classicCursorLibrary=null;
    }
    public Godot.Collections.Dictionary GetWindowInputAudit()=>new()
    {
        ["battleActive"]=battleWindowInput,["focused"]=!headless && inputWindow?.HasFocus()==true,
        ["desiredConfine"]=WantsBattleMouseConfined,["actualMouseMode"]=Input.MouseMode.ToString(),
        ["visibleCursor"]=OriginalGamePointerVisible || Input.MouseMode is Input.MouseModeEnum.Visible or Input.MouseModeEnum.Confined,
        ["resizable"]=inputWindow is not null && !inputWindow.Unresizable,["unresizable"]=inputWindow?.Unresizable??true,
        ["softwarePointer"]=softwareBattlePointer,["softwareMenuPointer"]=softwareMenuPointer,["originalCursor"]=classicCursorLibrary?.Audit()??new Godot.Collections.Dictionary(),["desktopMotion"]=desktopPointerMotion?.GetAudit()??new Godot.Collections.Dictionary(),["accumulatedInput"]=Input.UseAccumulatedInput,["nativeWarpSupported"]=!headless && DisplayServer.HasFeature(DisplayServer.Feature.MouseWarp),
        ["pointerOverlayVisible"]=battlePointerLayer?.Visible??false,["headless"]=headless,
        ["contentRect"]=BattleContentRect(),["pointerRect"]=BattlePointerRect(),["pointerInset"]=BattlePointerInset(),
        ["pointer"]=BattlePointerPosition(),["hardwarePointer"]=GetViewport().GetMousePosition(),
        ["battlefieldRect"]=battlefield is not null && GodotObject.IsInstanceValid(battlefield)?battlefield.GetGlobalRect():new Rect2(),
        ["edgeDirection"]=battlefield is not null && GodotObject.IsInstanceValid(battlefield)?BattleEdgeDirection(BattlePointerPosition()):Vector2.Zero,
        ["desiredCursor"]=OriginalGamePointerKey,["cursorShape"]=Input.GetCurrentCursorShape().ToString()
    };
}
