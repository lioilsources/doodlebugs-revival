Shader "Custom/SoftGlow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (0.5, 1, 1, 1)
        _GlowIntensity ("Intensity", Range(0, 3)) = 1.5
        _GlowSize ("Glow Size", Range(0, 0.1)) = 0.02
        _Softness ("Edge Softness", Range(0.001, 0.1)) = 0.03
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 2.0
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-1"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowSize;
            float _Softness;
            float _PulseSpeed;
            float _PulseAmount;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample surrounding pixels for blur/glow effect
                float2 texelSize = _MainTex_TexelSize.xy * (_GlowSize * 100.0);

                float alpha = 0;

                // 9-tap box blur for soft edges
                alpha += tex2D(_MainTex, i.uv + float2(-texelSize.x, -texelSize.y)).a;
                alpha += tex2D(_MainTex, i.uv + float2(0, -texelSize.y)).a;
                alpha += tex2D(_MainTex, i.uv + float2(texelSize.x, -texelSize.y)).a;
                alpha += tex2D(_MainTex, i.uv + float2(-texelSize.x, 0)).a;
                alpha += tex2D(_MainTex, i.uv).a;
                alpha += tex2D(_MainTex, i.uv + float2(texelSize.x, 0)).a;
                alpha += tex2D(_MainTex, i.uv + float2(-texelSize.x, texelSize.y)).a;
                alpha += tex2D(_MainTex, i.uv + float2(0, texelSize.y)).a;
                alpha += tex2D(_MainTex, i.uv + float2(texelSize.x, texelSize.y)).a;

                alpha /= 9.0;

                // Smooth the alpha for softer edges
                alpha = smoothstep(0, _Softness, alpha);

                // Pulse animation
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                // Final glow color
                fixed4 glowCol = _GlowColor;
                glowCol.rgb *= _GlowIntensity * pulse;
                glowCol.a = alpha * _GlowColor.a * i.color.a;

                return glowCol;
            }
            ENDCG
        }
    }
}
