Shader "Custom/SoftGlow"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (0.5, 1, 1, 0.6)
        _GlowIntensity ("Intensity", Range(0, 2)) = 1.0
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 1.0
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One // Additive blending for glow effect

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
            float4 _GlowColor;
            float _GlowIntensity;
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
                fixed4 texCol = tex2D(_MainTex, i.uv);

                // Calculate pulse effect using sine wave
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                // Apply glow color with intensity and pulse
                fixed4 glowCol = _GlowColor;
                glowCol.rgb *= _GlowIntensity * pulse;

                // Use texture alpha to mask the glow
                glowCol.a *= texCol.a * _GlowColor.a;

                // Apply vertex color (SpriteRenderer color tint)
                glowCol *= i.color;

                return glowCol;
            }
            ENDCG
        }
    }
}
