using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    Battlefield? battlefield;
    MiniMap? minimap;
    Label? creditsLabel;
    Label? powerLabel;
    Label? clockLabel;
    Label? statusLabel;
    Label? selectionLabel;
    Label? connectionLabel;
    Label? lobbyLabel;
    GridContainer? products;
    Button? supportButton;
    Button? cancelQueueButton;
    Button? startButton;
    BaseButton? repairButton;
    BaseButton? sellButton;
    ClassicPowerMeter? powerMeter;
    ScrollContainer? productionScroll;
    ClassicSidebarButtons? scrollUp;
    ClassicSidebarButtons? scrollDown;
    PopupPanel? battleOptions;
    Button? optionsButton;
    Control? battleSidebar;
    ClassicChrome? sidebarChrome;
    bool refreshingScroll;
    const float SidebarScale=Battlefield.ClassicZoom;
    const float ProductionTop=(110+69)*SidebarScale+FundsHeaderHeight;
    const int ProductionRow=75;
    const float SidebarBottom=26*SidebarScale;
    OptionButton? countryChoice;
    OptionButton? teamChoice;
    LineEdit? nameInput;
    LineEdit? addressInput;
    LineEdit? portInput;
    readonly Dictionary<string,BaseButton> productTabs=[];
    readonly List<(CameoButton Button,UnitType? Unit,BuildingType? Building)> productButtons=[];
    string productTab="build";
    string lastProductKey="";
    bool sovietUi;
    static readonly Color Gold=new("d5c28b");
    static readonly Color Pale=new("d9dde0");
    Theme? uiTheme;
    void ApplyUiTheme(bool soviet=false)
    {
        sovietUi=soviet;Theme=null;uiTheme?.Dispose();uiTheme=CreateTheme(soviet);Theme=uiTheme;
    }
    static Theme CreateTheme(bool soviet)
    {
        var t=new Theme{DefaultFontSize=14};
        var edge=new Color(soviet?"786363":"71818d");
        var steel=new Color(soviet?"34292b":"2c353e");
        var accent=new Color(soviet?"6a292d":"3c536a");
        void Box(string name,string type,StyleBoxFlat box){t.SetStylebox(name,type,box);box.Dispose();}
        foreach(var type in new[]{"Label","Button","LineEdit","OptionButton","PopupMenu"})t.SetColor("font_color",type,Pale);
        t.SetColor("font_hover_color","Button",Colors.White);t.SetColor("font_pressed_color","Button",Gold);
        t.SetColor("font_disabled_color","Button",new Color("76797e"));
        Box("normal","Button",Style(steel,edge,1));
        Box("hover","Button",Style(steel.Lightened(.15f),Gold,1));
        Box("pressed","Button",Style(accent,Gold,1));
        Box("hover_pressed","Button",Style(accent.Lightened(.12f),Gold,1));
        Box("disabled","Button",Style(new("1a1c21"),new("3b4048"),1));
        Box("focus","Button",Style(new Color(0,0,0,0),Gold,1));
        Box("panel","PanelContainer",Style(new("1b2027"),edge,1,10,8));
        Box("panel","PopupMenu",Style(new("181c23"),edge,1,6,5));
        Box("panel","PopupPanel",Style(new("181c23"),edge,1,0,0));
        Box("normal","LineEdit",Style(new("0c1016"),edge,1,6,4));
        Box("focus","LineEdit",Style(new Color(0,0,0,0),Gold,1,6,4));
        Box("scroll","VScrollBar",Style(new("11151a"),new("303842"),1,0,0));
        Box("grabber","VScrollBar",Style(steel.Lightened(.2f),edge,1,0,0));
        Box("grabber_highlight","VScrollBar",Style(accent.Lightened(.2f),Gold,1,0,0));
        Box("grabber_pressed","VScrollBar",Style(accent,Gold,1,0,0));
        Box("panel","TooltipPanel",Style(new("101217"),Gold,1,7,5));
        t.SetColor("font_color","TooltipLabel",new Color("eee6cd"));
        t.SetFontSize("font_size","TooltipLabel",14);
        t.SetConstant("separation","VBoxContainer",4);t.SetConstant("separation","HBoxContainer",4);
        t.SetConstant("h_separation","GridContainer",4);t.SetConstant("v_separation","GridContainer",4);
        t.SetConstant("scrollbar_width","VScrollBar",10);
        return t;
    }
    static StyleBoxFlat Style(Color background,Color border,int width,int horizontal=4,int vertical=3)
    {
        return new StyleBoxFlat{BgColor=background,BorderColor=border,BorderWidthLeft=width,BorderWidthRight=width,BorderWidthTop=width,BorderWidthBottom=width,ContentMarginLeft=horizontal,ContentMarginRight=horizontal,ContentMarginTop=vertical,ContentMarginBottom=vertical};
    }
    static T Full<T>(T control) where T:Control{control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);return control;}
    static Label Text(string text,int size=14,Color? color=null)
    {
        var label=new Label{Text=text};label.AddThemeFontSizeOverride("font_size",size);if(color is not null)label.AddThemeColorOverride("font_color",color.Value);return label;
    }
    static Button ActionButton(string text,Action action)
    {
        var b=new Button{Text=text,CustomMinimumSize=new Vector2(0,27),MouseDefaultCursorShape=CursorShape.PointingHand,FocusMode=FocusModeEnum.None};
        b.AddThemeFontSizeOverride("font_size",13);b.Pressed+=action;return b;
    }
    BaseButton InterfaceButton(string title,string name,Action action,int height)
    {
        var side=sovietUi?"soviet":"allied";var normal=Graphics.InterfaceFrame(side,name);
        if(normal is null)return ActionButton(title,action);
        // Measured in both sidec palettes: tab 1 lights the same glyph, 3 dims it,
        // 4 duplicates 0; 2 removes the glyph. Repair/sell have only 0 and a lit 1.
        // Use those observed appearances, with a small modulation for button-down.
        var lit=Graphics.InterfaceFrame(side,name,1)??normal;
        var disabled=name.StartsWith("tab",StringComparison.Ordinal)?Graphics.InterfaceFrame(side,name,3)??normal:normal;
        var button=new TextureButton{TextureNormal=normal,TexturePressed=lit,TextureHover=lit,TextureDisabled=disabled,IgnoreTextureSize=true,StretchMode=TextureButton.StretchModeEnum.Scale,CustomMinimumSize=new Vector2(0,height),FocusMode=FocusModeEnum.None,MouseDefaultCursorShape=CursorShape.PointingHand,TextureFilter=TextureFilterEnum.Nearest,TooltipText=title};
        button.ButtonDown+=()=>button.SelfModulate=new Color(.82f,.82f,.82f);
        button.ButtonUp+=()=>button.SelfModulate=Colors.White;
        button.Pressed+=action;
        return button;
    }
    static MarginContainer Inset(int left,int top,int right,int bottom)
    {
        var margin=new MarginContainer();margin.AddThemeConstantOverride("margin_left",left);margin.AddThemeConstantOverride("margin_right",right);margin.AddThemeConstantOverride("margin_top",top);margin.AddThemeConstantOverride("margin_bottom",bottom);return margin;
    }
    void ClearUi()
    {
        SetBattleWindowInput(false);ResetBattleChatUi();ResetMatchMenuUi();
        foreach(var child in GetChildren()){if(IsGameAudioNode(child) || child==battlePointerLayer)continue;RemoveChild(child);child.QueueFree();}
        battlefield=null;minimap=null;creditsLabel=null;powerLabel=null;clockLabel=null;statusLabel=null;selectionLabel=null;products=null;supportButton=null;cancelQueueButton=null;lobbyLabel=null;connectionLabel=null;startButton=null;
        countryChoice=null;teamChoice=null;nameInput=null;addressInput=null;portInput=null;productTabs.Clear();productButtons.Clear();
        repairButton=null;sellButton=null;powerMeter=null;productionScroll=null;scrollUp=null;scrollDown=null;battleOptions=null;optionsButton=null;battleSidebar=null;sidebarChrome=null;productPage=0;productPageCapacity=2;
    }
    void BuildFrontEnd()=>ShowFrontPage(network is null?FrontPage.Main:FrontPage.NetworkRoom);
    int UiTeam()=>(teamChoice?.GetSelectedId()??0)-1;
    void RefreshLobby()=>RefreshNetworkPage();
    void ShowBattle()
    {
        if(Battle is null)return;StopLanDiscovery();
        var local=Battle.Players[Math.Max(0,LocalPlayer)];
        ClearUi();ApplyUiTheme(Rules.Side(local.Setup.Country)==Faction.Soviet);lastProductKey="";productTab="build";
        var body=Full(new HBoxContainer());body.AddThemeConstantOverride("separation",0);AddChild(body);
        battlefield=new Battlefield{Controller=this,SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,ClipContents=true,FocusMode=FocusModeEnum.All};
        body.AddChild(battlefield);var spawn=Battle.Map.Spawns[Math.Max(0,LocalPlayer)];var focusRise=Battle.Map.OriginalDefinition is null?0:(float)Battle.Map.SurfaceHeightAt(spawn.X+.5,spawn.Y+.5)*.5f;battlefield.SetCameraCell(new Vector2(spawn.X+2-focusRise,spawn.Y+2-focusRise));
        battleSidebar=new Control{Name="BattleSidebar",CustomMinimumSize=new Vector2(252,0),SizeFlagsHorizontal=SizeFlags.ShrinkEnd,SizeFlagsVertical=SizeFlags.ExpandFill,ClipContents=true};body.AddChild(battleSidebar);
        var sideName=sovietUi?"soviet":"allied";
        // radar is a 33-frame opening sequence, not a set of mouse states.
        // The retained last frame exposes the full aperture used by the minimap.
        var radarArt=Graphics.InterfaceFrame(sideName,"radar",32);
        sidebarChrome=Full(new ClassicChrome{Kind=ClassicChrome.SurfaceKind.Sidebar,Soviet=sovietUi,RadarTexture=radarArt,TopTexture=Graphics.InterfaceFrame(sideName,"side1"),MiddleTexture=Graphics.InterfaceFrame(sideName,"side2"),BottomTexture=Graphics.InterfaceFrame(sideName,"side3")});sidebarChrome.OffsetTop=FundsHeaderHeight;battleSidebar.AddChild(sidebarChrome);
        // Reuse the retained radar's empty lower strip for the money header, so
        // its native metal edges match the faction and no replacement skin is needed.
        if(radarArt is not null)battleSidebar.AddChild(new TextureRect{Name="SidebarFundsFrame",Texture=new AtlasTexture{Atlas=radarArt,Region=new Rect2(0,97,168,13)},Size=new Vector2(252,FundsHeaderHeight),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore,TextureFilter=TextureFilterEnum.Nearest});
        minimap=new MiniMap{Controller=this,View=battlefield,Position=new Vector2(19.5f,FundsHeaderHeight),Size=new Vector2(211.5f,165)};battleSidebar.AddChild(minimap);
        creditsLabel=Text("",14,Gold);creditsLabel.Name="SidebarFunds";creditsLabel.Position=new Vector2(33,0);creditsLabel.Size=new Vector2(183,FundsHeaderHeight);creditsLabel.HorizontalAlignment=HorizontalAlignment.Center;creditsLabel.VerticalAlignment=VerticalAlignment.Center;creditsLabel.MouseFilter=MouseFilterEnum.Pass;battleSidebar.AddChild(creditsLabel);
        repairButton=InterfaceButton("维修","repair",()=>ToggleMaintenance(CommandType.Repair),0);repairButton.Name="SidebarRepair";repairButton.ToggleMode=true;repairButton.TooltipText="维修模式：左键点击己方建筑；右键 / Esc 取消";battleSidebar.AddChild(repairButton);
        sellButton=InterfaceButton("出售","sell",()=>ToggleMaintenance(CommandType.Sell),0);sellButton.Name="SidebarSell";sellButton.ToggleMode=true;sellButton.TooltipText="出售模式：左键点击己方建筑；右键 / Esc 取消";battleSidebar.AddChild(sellButton);
        // Preserve each faction's original repair/sell/tab aspect and placement.
        var repairTexture=Graphics.InterfaceFrame(sideName,"repair");var sellTexture=Graphics.InterfaceFrame(sideName,"sell");
        var maintenanceTop=(sovietUi?178.5f:181.5f)+FundsHeaderHeight;
        repairButton.Position=new Vector2(31.5f,maintenanceTop);repairButton.Size=(repairTexture?.GetSize()??new Vector2(64,31))*SidebarScale;
        sellButton.Position=new Vector2(127.5f,maintenanceTop);sellButton.Size=(sellTexture?.GetSize()??new Vector2(64,31))*SidebarScale;
        var tabIndex=0;var tabWidth=sovietUi?48f:42f;var tabLeft=sovietUi?28.5f:40.5f;
        foreach(var tab in new[]{("build","建造"),("defense","防御"),("infantry","步兵"),("vehicle","战车")})
        {
            var key=tab.Item1;var index=tabIndex++;var name=$"tab{index:00}";var texture=Graphics.InterfaceFrame(sideName,name);
            var button=InterfaceButton(tab.Item2,name,()=>{productTab=key;productPage=0;lastProductKey="";RefreshProducts();},0);
            button.ToggleMode=true;button.Position=new Vector2(tabLeft+index*tabWidth,ProductionTop-(texture?.GetHeight()??27)*SidebarScale);button.Size=(texture?.GetSize()??new Vector2(28,27))*SidebarScale;battleSidebar.AddChild(button);productTabs[key]=button;
        }
        // Native cameo canvases are 60x48. side2 repeats every 50px, with columns
        // at x=22/84 (Allied) or 21/85 (Soviet), not the old 96x78 + 4px grid.
        productionScroll=new ScrollContainer{Name="ProductionCatalog",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.ShowNever,MouseForcePassScrollEvents=false,FocusMode=FocusModeEnum.None,DrawFocusBorder=false};
        using(var empty=new StyleBoxEmpty())productionScroll.AddThemeStyleboxOverride("panel",empty);
        battleSidebar.AddChild(productionScroll);productionScroll.GuiInput+=HandleCatalogWheel;
        battleSidebar.GuiInput+=input=>
        {
            if(input is InputEventMouseButton{Pressed:true} mouse && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mouse.Position.Y>=ProductionTop && mouse.Position.Y<battleSidebar.Size.Y-SidebarBottom)
            {ScrollProducts(mouse.ButtonIndex==MouseButton.WheelUp?-1:1);battleSidebar.AcceptEvent();}
        };
        products=new GridContainer{Columns=2,SizeFlagsHorizontal=SizeFlags.ShrinkBegin};products.AddThemeConstantOverride("h_separation",sovietUi?6:3);products.AddThemeConstantOverride("v_separation",3);productionScroll.AddChild(products);
        productionScroll.GetVScrollBar().Step=1;
        productionScroll.GetVScrollBar().ValueChanged+=_=>RefreshSidebarScroll();
        products.MinimumSizeChanged+=()=>CallDeferred(nameof(RefreshSidebarScroll));
        powerMeter=new ClassicPowerMeter{Position=new Vector2(13.5f,ProductionTop),Size=new Vector2(12,75)};battleSidebar.AddChild(powerMeter);
        scrollUp=new ClassicSidebarButtons{Name="SidebarPageUp",TooltipText="上一页",Down=false};scrollUp.Pressed+=()=>ScrollProducts(-1);battleSidebar.AddChild(scrollUp);
        scrollDown=new ClassicSidebarButtons{Name="SidebarPageDown",TooltipText="下一页",Down=true};scrollDown.Pressed+=()=>ScrollProducts(1);battleSidebar.AddChild(scrollDown);
        battleSidebar.Resized+=LayoutBattleSidebar;LayoutBattleSidebar();
        BuildBattleChat();BuildMatchMenu();AudioBattle();
        RefreshProducts();RefreshHud();SetBattleWindowInput(true);
    }
    void LayoutBattleSidebar()
    {
        if(battleSidebar is null || productionScroll is null || powerMeter is null || scrollUp is null || scrollDown is null)return;
        var left=(sovietUi?21:22)*SidebarScale;var width=sovietUi?186:183;
        var available=Math.Max(0,battleSidebar.Size.Y-SidebarBottom-ProductionTop-1.5f);
        var rows=Math.Max(1,(int)Math.Floor((available+3)/ProductionRow));
        productPageCapacity=rows*2;
        var height=Math.Min(available,rows*ProductionRow-3);
        productionScroll.Position=new Vector2(left,ProductionTop+1.5f);productionScroll.Size=new Vector2(width,height);
        powerMeter.Size=new Vector2(12,Math.Max(0,battleSidebar.Size.Y-SidebarBottom-ProductionTop));
        var footer=Math.Max(ProductionTop,battleSidebar.Size.Y-SidebarBottom);
        scrollUp.Position=new Vector2(left,footer+3);scrollUp.Size=new Vector2(90,33);
        scrollDown.Position=new Vector2(left+(sovietUi?96:93),footer+3);scrollDown.Size=new Vector2(90,33);
        RefreshSidebarScroll();
    }
    void ScrollProducts(int direction)
    {
        if(productionScroll is null || direction==0)return;
        productPage=Math.Clamp(productPage+Math.Sign(direction),0,ProductPageCount-1);
        RefreshSidebarScroll();
    }
    void RefreshSidebarScroll()
    {
        if(productionScroll is null || scrollUp is null || scrollDown is null || refreshingScroll)return;
        refreshingScroll=true;
        try{RefreshProductPage();}
        finally{refreshingScroll=false;}
    }
    void ReturnToDeployment()
    {
        terrainInspection=terrainInspectionMotion=fullPlaythroughControlsEnemy=fullPlaythroughPaused=false;
        SetBattleWindowInput(false);CancelLocalPreparation();StopExternalAi();network?.Dispose();network=null;localWorld=null;observedMatch=null;localPending.Clear();localReplay.Clear();failure="";notice="准备就绪";accumulator=0;BuildFrontEnd();AudioMenu();
    }
    void RefreshHud()
    {
        if(Battle is null){RefreshLobby();return;}
        RefreshBattleChat();
        if(creditsLabel is null || battlefield is null)return;
        var w=Battle;var p=w.Players[Math.Max(0,LocalPlayer)];
        var clock=$"作战时间  {w.Tick/1200:00}:{w.Tick/20%60:00}";
        var power=$"电力产出 {p.GeneratedPower} / 需求 {p.UsedPower}";
        creditsLabel.Text=IsSpectating?"全局观战":$"{p.Credits:N0}";
        creditsLabel.TooltipText=$"资金：{p.Credits:N0}\n{power}\n{clock}";
        // The former toolbar labels are optional. Their information remains in
        // sidebar tooltips; no hidden detached controls or top-left HUD are built.
        if(powerLabel is not null){powerLabel.Text=power;powerLabel.Modulate=p.Powered?Pale:new Color("ff8066");}
        if(clockLabel is not null){clockLabel.Text=clock;clockLabel.TooltipText=network is null?"四国训练场":$"{w.Players.Length} 人联机";}
        powerMeter?.SetPower(p.GeneratedPower,p.UsedPower);
        battlefield.Selection.RemoveWhere(id=>w.Find(id)==null);
        var selected=battlefield.Selection.Select(w.Find).Where(e=>e is not null).ToArray();
        string selectionText;
        if(selected.Length==1)
        {
            var e=selected[0]!;
            if(e is Building b)
            {
                var state=!b.Complete?$"建造 {(int)(b.ConstructionProgress*100)}%":b.Queue.Count>0?$"{Rules.Unit(b.Queue[0]).Name} · 队列 {b.Queue.Count}":b.Repairing?"维修中":"右键设置集结点";
                selectionText=$"{b.Definition.Name}  {b.Health}/{b.MaxHealth}\n{state}";
            }
            else
            {
                var u=(Unit)e;var hint=u.Definition.Miner?$"装载 {u.CargoBales}/{RedAlert.Simulation.World.HarvesterStorageBales(u.Type)} · 货值 ${u.Cargo}":u.Type==UnitType.Mcv?"D 展开基地 / 右键点击自身展开":u.Definition.Passengers>0?$"载员 {u.Passengers.Count}/{u.Definition.Passengers} · D 卸载":u.Definition.CanDeploy?"D 展开 / 收起 · 右键移动和攻击":"右键移动 / 攻击目标";selectionText=$"{u.Definition.Name}  {e.Health}/{e.MaxHealth}\n{hint}";
            }
        }
        else selectionText=selected.Length==0?"选择单位或建筑\nCtrl + 数字编组 / 数字选择":$"已选 {selected.Length} 个作战单位\nA + 左键：进攻移动";
        if(selectionLabel is not null){selectionLabel.TooltipText=selectionText;selectionLabel.Text=selectionText.Split('\n')[0];}
        var ownBuilding=selected.OfType<Building>().FirstOrDefault(b=>b.Owner==LocalPlayer);
        var canAct=LocalPlayer>=0 && !IsSpectating;
        if(IsSpectating)battlefield.CancelMaintenance();
        RefreshMaintenanceButtons();
        if(cancelQueueButton is not null)cancelQueueButton.Disabled=!canAct || ownBuilding is null || ownBuilding.Queue.Count==0 || ownBuilding.Type is not (BuildingType.Barracks or BuildingType.WarFactory);
        if(supportButton is not null)
        {
            supportButton.Visible=!IsSpectating && p.Setup.Country==Country.America;
            supportButton.Disabled=!canAct || p.SupportReadyTick<0 || w.Tick<p.SupportReadyTick || !w.Buildings.Any(b=>b.Owner==LocalPlayer && b.Definition.AirCapacity>0 && w.ClassicBuildingOperational(b));
            supportButton.Text=w.Tick<p.SupportReadyTick?$"空降伞兵 {(p.SupportReadyTick-w.Tick+19)/20}s":"空降伞兵";
            supportButton.TooltipText="美国支援技能\n需求：兵营、冷却完成\n点击后在可见地面选择投放位置";
        }
        if(stopMenuButton is not null)stopMenuButton.Disabled=!canAct || !selected.OfType<Unit>().Any(u=>u.Owner==LocalPlayer);
        if(cancelBuildingMenuButton is not null)cancelBuildingMenuButton.Disabled=!canAct || p.BuildingProduction is null;
        var status=w.Finished?w.WinningTeam==p.Setup.Team?"作战胜利 · 可保存录像 / 返回":"作战结束 · 可保存录像 / 返回":
            IsSpectating?"全局观战 · Esc 打开菜单":notice;
        if(statusLabel is not null){statusLabel.TooltipText=status;statusLabel.Text=status.Replace('\n',' ');}
        if(battleSidebar is not null)battleSidebar.TooltipText=$"{selectionText}\n{status}\nEsc：作战菜单";
        RefreshProducts();RefreshProductProgress();RefreshSidebarScroll();
    }
    void RefreshProducts()=>RefreshClassicProducts();
    void RefreshProductProgress()=>RefreshClassicProductProgress();
    void SelectBuildingProduct(BuildingType type)
    {
        if(Battle is null || LocalPlayer<0 || IsSpectating)return;
        var player=Battle.Players[LocalPlayer];
        if(player.BuildingProduction==type)
        {
            if(player.BuildingProductionPaused){Issue(new GameCommand{Type=CommandType.ResumeBuilding});return;}
            if(!player.BuildingReady){Notify($"正在准备{Rules.Building(type,player.Setup.Country).Name}，就绪后再选择位置");return;}
            battlefield!.CancelMaintenance();battlefield.Placement=type;battlefield.SupportMode=false;battlefield.AttackMode=false;
            Notify($"{Rules.Building(type,player.Setup.Country).Name}已就绪：左键选址，右键暂时收起");
        }
        else Issue(new GameCommand{Type=CommandType.QueueBuilding,Building=type});
    }
    void Train(UnitType type)=>TrainClassicProduct(type);
    public void StopSelection()
    {
        if(Battle is null || battlefield is null)return;
        var ids=battlefield.Selection.Select(Battle.Find).OfType<Unit>().Where(u=>u.Owner==LocalPlayer).Select(u=>u.Id).ToArray();
        if(ids.Length>0)Issue(new GameCommand{Type=CommandType.Stop,Actors=ids});
    }
    void BuildingAction(CommandType type)
    {
        if(Battle is null || battlefield is null)return;
        var b=battlefield.Selection.Select(Battle.Find).OfType<Building>().FirstOrDefault(b=>b.Owner==LocalPlayer);
        if(b is null){Notify("先选择一栋自己的建筑");return;}
        Issue(new GameCommand{Type=type,Actors=[b.Id]});
    }
    void SaveReplay()
    {
        if(Battle is null)return;
        try
        {
            var path=ProjectSettings.GlobalizePath($"user://Replays/match-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var replay=network?.Replay()??new ReplayFile{Settings=Battle.Settings,Commands=localReplay,FinalTick=Battle.Tick,FinalHash=Battle.StateHash().ToString("x16")};
            replay.Save(path);Notify("录像已保存到用户数据目录 Replays");
        }
        catch(Exception ex){Notify("保存失败："+ex.Message);}
    }
    void UpdateCamera(double delta)
    {
        if(battlefield is null || !battlefield.IsInsideTree() || !battlefield.IsVisibleInTree() || ChatTyping || MatchMenuOpen || battleOptions?.Visible==true || minimap?.DraggingViewport==true || (!headless && !GetWindow().HasFocus()))return;
        var direction=Vector2.Zero;
        if(Input.IsPhysicalKeyPressed(Key.Left))direction.X--;
        if(Input.IsPhysicalKeyPressed(Key.Right))direction.X++;
        if(Input.IsPhysicalKeyPressed(Key.Up))direction.Y--;
        if(Input.IsPhysicalKeyPressed(Key.Down))direction.Y++;
        // Scroll at the outer client edges, including the sidebar's right side.
        // The battlefield/sidebar seam is an ordinary interior pointer position.
        // Hit testing hovered children would miss the outer sidebar frame.
        if(!headless && clientPreferences.EdgeScrolling)
            direction+=BattleEdgeDirection(BattlePointerPosition());
        if(direction!=Vector2.Zero)battlefield.PanScreen(direction.LimitLength()*650*clientPreferences.CameraSpeed*(float)delta);
    }
}
