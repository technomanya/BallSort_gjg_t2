// UI/Default with an HSV boost on the pink/magenta hues only (the gold frames and highlights keep their colour),
// for kit sprites that a mockup shows in a more saturated magenta than the exported PNGs (Mana Flip).
// _Saturation blends the saturation towards 1 (0 = unchanged), _Brightness multiplies the value.
// No stencil mask or clip rect support.
Shader "BallSort/UI Saturate"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Saturation ("Saturation (towards full)", Range(0, 1)) = 0
        _Brightness ("Brightness", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Saturation;
            float _Brightness;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = c.g < c.b ? float4(c.bg, K.wz) : float4(c.gb, K.xy);
                float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.texcoord);
                float3 hsv = rgb2hsv(col.rgb);
                // 1 inside the magenta band (hue 270..350 deg), fading to 0 twenty degrees outside it
                float hueDist = abs(hsv.x - 0.86);
                float w = 1.0 - smoothstep(0.11, 0.17, hueDist);
                hsv.y = lerp(hsv.y, 1.0, _Saturation * w);
                hsv.z = saturate(hsv.z * lerp(1.0, _Brightness, w));
                col.rgb = hsv2rgb(hsv);
                col *= i.color;
                clip(col.a - 0.001);
                return col;
            }
            ENDCG
        }
    }
}
