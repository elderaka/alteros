Shader "AlterOS/Furniture/PowerStateFixture"
{
    Properties
    {
        [HideInInspector] _FurniturePowerState ("Committed Furniture Power State", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _FurniturePowerState;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                int state = (int)floor(_FurniturePowerState + 0.5);
                if (state == 0) return fixed4(0.44, 0.48, 0.53, 1.0);
                if (state == 1) return fixed4(0.08, 0.10, 0.12, 1.0);
                if (state == 2) return fixed4(0.12, 0.72, 0.88, 1.0);
                return fixed4(0.86, 0.14, 0.72, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}