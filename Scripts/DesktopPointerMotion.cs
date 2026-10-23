// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2025 Huawei Device Co., Ltd.
// C# adaptation of the OpenHarmony pointer-motion formulas and configuration:
// https://github.com/openharmony/multimodalinput_input/tree/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0 .
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the
// specific language governing permissions and limitations under the License.

using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RedAlert;

/// <summary>
/// Converts API 26 captured hardware counts to desktop screen-pixel increments.
/// Apply to original, unaccumulated reports before viewport scaling and clipping.
/// This helper reads settings; it does not change mouse mode or input accumulation.
/// </summary>
public sealed class DesktopPointerMotion
{
    const string ProfilePath="/system/etc/multimodalinput/pointer_motion_acceleration_config.json";
    const string Upstream="openharmony/multimodalinput_input@7f674c11dee34d4c158827ca8f0e8db3b12a1d4b";
    const int DefaultPointerSpeed=10; // MousePreferenceAccessor::DEFAULT_SPEED.
    readonly int fallbackPointerSpeed;
    readonly bool openHarmony;
    double[] speeds=DefaultSpeeds();
    double[] slow=[1.0341957429588597,.7126536301164397,2.5813850030026853,.02004352388383787];
    double[] fast=[1.3556224243118067,2.5417186406523578,6.127531547651636,3.2322785269552803];
    double standardPpi=264.16;
    Curve[] pcCurves=DefaultPcCurves();
    string profileSource="pinned_upstream_defaults",profileError="";
    string speedSource="system_default_fallback",speedError="",displayError="";
    int nativeSpeedResult=-1,displayXResult=-1,displayYResult=-1;
    int densityDpi;
    float densityPixels,xDpi,yDpi;
    Vector2I displaySize;
    double physicalPpi,diagonalInches,screenFactor=1;
    double lastGain=1;
    Vector2 lastRaw,lastScreen;
    long reports;

    public int PointerSpeed {get;private set;}

    public DesktopPointerMotion(int fallbackPointerSpeed=DefaultPointerSpeed)
    {
        if(fallbackPointerSpeed is <1 or >20)throw new ArgumentOutOfRangeException(nameof(fallbackPointerSpeed));
        this.fallbackPointerSpeed=fallbackPointerSpeed;
        openHarmony=string.Equals(OS.GetName(),"OpenHarmony",StringComparison.OrdinalIgnoreCase)
            && !string.Equals(DisplayServer.GetName(),"headless",StringComparison.OrdinalIgnoreCase);
        ReadProfile();
        RefreshNativeSettings();
    }

    /// <summary>Read-only requery after focus or desktop preference changes.</summary>
    public void RefreshNativeSettings()
    {
        PointerSpeed=fallbackPointerSpeed;
        speedSource=fallbackPointerSpeed==DefaultPointerSpeed?"system_default_fallback":"caller_fallback";
        nativeSpeedResult=-1;speedError="";displayError="";
        physicalPpi=0;diagonalInches=0;screenFactor=1;
        displaySize=Vector2I.Zero;xDpi=yDpi=densityPixels=0;densityDpi=0;
        displayXResult=displayYResult=-1;
        if(!openHarmony)return;
        try
        {
            var instance=Native.GetInputManager();
            if(instance!=IntPtr.Zero)
            {
                nativeSpeedResult=Native.GetPointerSpeed(instance,out var value);
                if(nativeSpeedResult==0 && value>=1 && value<=20){PointerSpeed=value;speedSource="native_mmi";}
                else speedError=$"native_result={nativeSpeedResult}; value={value}";
            }
            else speedError="native_instance_unavailable";
        }
        catch(Exception exception) when(IsInteropFailure(exception)){speedError=exception.GetType().Name;}
        try
        {
            var widthResult=Native.GetWidth(out var width);var heightResult=Native.GetHeight(out var height);
            displaySize=widthResult==0 && heightResult==0 && width>0 && height>0?new Vector2I(width,height):DisplayServer.ScreenGetSize();
            if(Native.GetDensityDpi(out var dpi)==0)densityDpi=dpi;
            if(Native.GetDensityPixels(out var density)==0)densityPixels=density;
            displayXResult=Native.GetXDpi(out xDpi);displayYResult=Native.GetYDpi(out yDpi);
            if(displayXResult==0 && displayYResult==0 && xDpi>0 && yDpi>0 && float.IsFinite(xDpi) && float.IsFinite(yDpi)
                && displaySize.X>0 && displaySize.Y>0)
            {
                // MMI uses pixel diagonal / physical diagonal, not densityDpi.
                var inchWidth=displaySize.X/(double)xDpi;var inchHeight=displaySize.Y/(double)yDpi;
                diagonalInches=Math.Sqrt(inchWidth*inchWidth+inchHeight*inchHeight);
                physicalPpi=Math.Sqrt((double)displaySize.X*displaySize.X+(double)displaySize.Y*displaySize.Y)/diagonalInches;
                screenFactor=ScreenFactor((int)diagonalInches);
            }
        }
        catch(Exception exception) when(IsInteropFailure(exception)){displayError=exception.GetType().Name;}
    }

    /// <summary>Returns physical screen displacement, with no viewport/density transform.</summary>
    public Vector2 ToScreenPixels(Vector2 rawCounts)
    {
        if(!float.IsFinite(rawCounts.X) || !float.IsFinite(rawCounts.Y))return Vector2.Zero;
        lastRaw=rawCounts;
        lastGain=openHarmony?Gain(rawCounts):1;
        lastScreen=rawCounts*(float)lastGain;reports++;
        return lastScreen;
    }

    double Gain(Vector2 raw)
    {
        if(raw==Vector2.Zero)return 1;
        double gain;
        if(physicalPpi>0)
        {
            var vin=Math.Sqrt((double)raw.X*raw.X+(double)raw.Y*raw.Y);
            var t=vin<13.5?slow[0]*Math.Log(slow[1]*vin+slow[2])+slow[3]
                :fast[0]*Math.Log(fast[1]*Math.Log(vin)-fast[2])+fast[3];
            gain=t*speeds[PointerSpeed-1]*(physicalPpi/standardPpi)*screenFactor;
        }
        else
        {
            // The OS falls back to device curves when physical PPI is unavailable.
            // This project targets the PC branch; touchpad/device variants are separate.
            var vin=Math.Max(Math.Abs((double)raw.X),Math.Abs((double)raw.Y))
                +Math.Min(Math.Abs((double)raw.X),Math.Abs((double)raw.Y))/2;
            var curve=pcCurves[PointerSpeed<=pcCurves.Length?PointerSpeed-1:pcCurves.Length-1];
            var segment=curve.Speeds.Length-1;
            for(var i=0;i<curve.Speeds.Length;i++)if(vin<=curve.Speeds[i]){segment=i;break;}
            gain=(curve.Slopes[segment]*vin+curve.Offsets[segment])/vin;
        }
        return double.IsFinite(gain) && gain>0?gain:1;
    }

    public Godot.Collections.Dictionary GetAudit()=>new()
    {
        ["upstream"]=Upstream,["profileSource"]=profileSource,["profileError"]=profileError,
        ["openHarmony"]=openHarmony,["profileBranch"]=!openHarmony?"identity":physicalPpi>0?"MouseDynamic":"PCMouse_fallback",
        ["pointerSpeed"]=PointerSpeed,["pointerSpeedSource"]=speedSource,["nativeSpeedResult"]=nativeSpeedResult,["nativeSpeedError"]=speedError,
        ["defaultPointerSpeed"]=DefaultPointerSpeed,["physicalPpi"]=physicalPpi,["standardPpi"]=standardPpi,
        ["xDpi"]=xDpi,["yDpi"]=yDpi,["densityDpiUnused"]=densityDpi,["densityPixelsUnused"]=densityPixels,
        ["displaySize"]=displaySize,["displaySource"]="default_native_display",["displayXResult"]=displayXResult,["displayYResult"]=displayYResult,
        ["displayError"]=displayError,["diagonalInches"]=diagonalInches,["screenFactor"]=screenFactor,
        ["accelerationSwitchSource"]="assumed_enabled_no_public_getter",
        ["useAccumulatedInput"]=Input.UseAccumulatedInput,["requiresUnaccumulatedReports"]=true,
        ["reports"]=reports,["lastRawCounts"]=lastRaw,["lastScreenPixels"]=lastScreen,["lastGain"]=lastGain,
        ["gainAt1"]=openHarmony?Gain(new Vector2(1,0)):1,["gainAt8"]=openHarmony?Gain(new Vector2(8,0)):1,
        ["gainAt16"]=openHarmony?Gain(new Vector2(16,0)):1,["gainAt32"]=openHarmony?Gain(new Vector2(32,0)):1
    };

    void ReadProfile()
    {
        if(!openHarmony)return;
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(ProfilePath));
            var dynamicProfile=document.RootElement.GetProperty("MouseDynamic");
            var parsedSpeeds=ReadNumbers(dynamicProfile,"speeds",20);var parsedSlow=ReadNumbers(dynamicProfile,"slow_gains",4);
            var parsedFast=ReadNumbers(dynamicProfile,"fast_gains",4);var parsedPpi=dynamicProfile.GetProperty("standard_ppi").GetDouble();
            if(!double.IsFinite(parsedPpi) || parsedPpi<=0)throw new FormatException("Invalid standard_ppi");
            var entries=document.RootElement.GetProperty("PCMouse");
            if(entries.GetArrayLength()==0)throw new FormatException("Missing PCMouse curves");
            var parsedCurves=new Curve[entries.GetArrayLength()];
            for(var i=0;i<parsedCurves.Length;i++)
            {
                var item=entries[i];var thresholds=item.GetProperty("speeds").GetArrayLength();
                if(thresholds<1)throw new FormatException("Empty PCMouse curve");
                parsedCurves[i]=new Curve(ReadNumbers(item,"speeds",thresholds),ReadNumbers(item,"slopes",thresholds),ReadNumbers(item,"diff_nums",thresholds));
            }
            speeds=parsedSpeeds;slow=parsedSlow;fast=parsedFast;standardPpi=parsedPpi;pcCurves=parsedCurves;
            profileSource=ProfilePath;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {profileError=exception.GetType().Name;}
    }

    static double[] ReadNumbers(JsonElement parent,string property,int expected)
    {
        var source=parent.GetProperty(property);
        if(source.GetArrayLength()!=expected)throw new FormatException($"Invalid {property} length");
        var values=new double[expected];
        for(var i=0;i<expected;i++){values[i]=source[i].GetDouble();if(!double.IsFinite(values[i]))throw new FormatException($"Invalid {property}");}
        return values;
    }
    static bool IsInteropFailure(Exception exception)=>exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException or PlatformNotSupportedException;
    static double ScreenFactor(int diagonal)=>diagonal<=0?1:diagonal<8?.7:diagonal<18?1:diagonal<27?1.2:diagonal<55?1.6:2.4;
    static double[] DefaultSpeeds()=>[.1,.2,.3,.4,.5,.6,.7,.8,.9,1,1.1,1.2,1.3,1.4,1.5,1.6,1.7,1.8,1.9,2];
    sealed record Curve(double[] Speeds,double[] Slopes,double[] Offsets);
    static Curve[] DefaultPcCurves()=>
    [
        new([8,32,128],[.16,.30,.56],[0,-1.12,-9.44]),new([8,32,128],[.32,.60,1.12],[0,-2.24,-18.88]),
        new([8,32,128],[.64,1.2,2.24],[0,-4.48,-37.76]),new([8,32,128],[.80,1.50,2.80],[0,-5.6,-47.2]),
        new([8,32,128],[.92,2.40,4.48],[0,-11.84,-78.4]),new([8,32,128],[1.04,3.30,6.16],[0,-18.08,-109.60]),
        new([8,32,128],[1.10,3.75,7.00],[0,-21.2,-125.20]),new([8,32,128],[1.16,4.20,7.84],[0,-24.32,-140.8]),
        new([8,32,128],[1.22,4.65,8.68],[0,-27.44,-156.40]),new([8,32,128],[1.28,5.1,9.52],[0,-30.56,-172]),
        new([8,32,128],[1.34,5.55,10.36],[0,-33.68,-187.6])
    ];

    static class Native
    {
        const string Mmi="/system/lib64/platformsdk/libmmi-client.z.so";
        const string Display="libnative_display_manager.so";
        [DllImport(Mmi,EntryPoint="_ZN4OHOS3MMI12InputManager11GetInstanceEv",CallingConvention=CallingConvention.Cdecl)]
        internal static extern IntPtr GetInputManager();
        [DllImport(Mmi,EntryPoint="_ZN4OHOS3MMI12InputManager15GetPointerSpeedERi",CallingConvention=CallingConvention.Cdecl)]
        internal static extern int GetPointerSpeed(IntPtr instance,out int speed);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayWidth")]
        internal static extern int GetWidth(out int value);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayHeight")]
        internal static extern int GetHeight(out int value);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayDensityDpi")]
        internal static extern int GetDensityDpi(out int value);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayDensityPixels")]
        internal static extern int GetDensityPixels(out float value);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayDensityXdpi")]
        internal static extern int GetXDpi(out float value);
        [DllImport(Display,EntryPoint="OH_NativeDisplayManager_GetDefaultDisplayDensityYdpi")]
        internal static extern int GetYDpi(out float value);
    }
}
