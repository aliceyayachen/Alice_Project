// Draws the floor blob as a soft, glowing, wobbling shape (same maths as the "Floor Blob Look" browser preview).
// Used on a flat quad that BlobLook.cs creates under the blob. Built-in Render Pipeline.
Shader "Exhibit/FluidBlob"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.949, 0.541, 1)
        _Extent ("Quad extent (in blob radii)", Float) = 3.2
        _Wobble ("Wobble", Float) = 0.25
        _WobbleTime ("Wobble time (set by script)", Float) = 0
        _Soft ("Edge softness", Float) = 0.17
        _Halo ("Halo size", Float) = 0.75
        _Glow ("Halo strength", Float) = 0.43
        _Deform ("Shape deform (set by script)", Vector) = (1, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha   // premultiplied: glows over the dark floor
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Extent, _Wobble, _WobbleTime, _Soft, _Halo, _Glow;
            float4 _Deform;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // Position in "blob radii", then undo the squash/stretch.
                float2 q = (i.uv - 0.5) * 2.0 * _Extent;
                float2 p = float2(_Deform.x * q.x + _Deform.y * q.y,
                                  _Deform.z * q.x + _Deform.w * q.y);

                float r = length(p);
                float a = atan2(p.y, p.x);
                float t = _WobbleTime;

                // Slowly drifting ripples around the edge give the liquid outline.
                float w = sin(2.0 * a + t * 0.9) * 0.5
                        + sin(3.0 * a - t * 1.3 + 1.7) * 0.35
                        + sin(5.0 * a + t * 2.1 + 0.4) * 0.2
                        + sin(7.0 * a - t * 2.7 + 2.2) * 0.12;

                float d = r - (1.0 + _Wobble * w);
                float body = 1.0 - smoothstep(-_Soft, _Soft, d);

                // The glow has its own, bigger and slower ripples, so it sloshes around
                // the blob like a liquid instead of sitting there as a round fog.
                float wh = sin(2.0 * a - t * 0.6 + 0.8) * 0.5
                         + sin(3.0 * a + t * 0.8 + 2.4) * 0.35
                         + sin(4.0 * a - t * 1.1 + 1.1) * 0.25;
                float dh = r - (1.0 + _Wobble * (w * 1.3 + wh * 0.9));
                float hx = max(dh + _Soft * 0.5, 0.0) / max(_Halo, 0.001);
                float halo = exp(-pow(hx, 1.4)) * _Glow;
                // Fade the glow out following that same wobbly outline (not a circle).
                halo *= 1.0 - smoothstep(_Extent * 0.4, _Extent * 0.7, dh);
                float core = exp(-r * r * 2.2);

                float3 col = _Color.rgb * body * (0.7 + 0.3 * core)
                           + float3(1.0, 0.99, 0.9) * core * body * 0.3
                           + _Color.rgb * halo * 0.8 * (1.0 - body * 0.6);
                // Safety net: nothing at the very border of the square it's drawn on.
                float2 e = abs(i.uv - 0.5) * 2.0;
                col *= 1.0 - smoothstep(0.88, 1.0, max(e.x, e.y));

                float alpha = saturate(max(max(col.r, col.g), col.b));
                return float4(col, alpha);
            }
            ENDCG
        }
    }
}
