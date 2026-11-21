#if OPENGL
    #define SV_POSITION POSITION
    #define VS_MODEL vs_3_0
    #define PS_MODEL ps_3_0
#else
    #define VS_MODEL vs_4_0
    #define PS_MODEL ps_4_0
#endif

float4x4 ViewProjection;
float3 LightDirection = float3(-0.45, -0.82, 0.35);
float3 AmbientColor = float3(0.66, 0.69, 0.62);
float3 Tint = float3(1, 1, 1);
float4 LeafColor = float4(0, 0, 0, 0);
float2 CrownProfile = float2(0.78, 1.35);
float Opacity;
texture Palette;
sampler PaletteSampler = sampler_state
{
    Texture = <Palette>;
    MinFilter = LINEAR; MagFilter = LINEAR; MipFilter = LINEAR;
    AddressU = CLAMP; AddressV = CLAMP;
};
struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 UV : TEXCOORD0;
    float4 Instance0 : TEXCOORD4;
    float4 Instance1 : TEXCOORD5;
    float4 Instance2 : TEXCOORD6;
    float4 Instance3 : TEXCOORD7;
};
struct PixelInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
    float3 Lighting : TEXCOORD1;
};
PixelInput InstancedVS(VertexInput input)
{
    PixelInput output;
    float4x4 world = float4x4(input.Instance0, input.Instance1, input.Instance2, input.Instance3);
    float4 p = mul(input.Position, world);
    float3 treePosition = input.Position.xyz;
    float3 crownNormal = normalize(float3(treePosition.x, (treePosition.y - CrownProfile.y) * 0.78 + 0.001, treePosition.z));
    float3 localNormal = lerp(input.Normal, crownNormal, LeafColor.a * 0.82);
    float3 normal = normalize(mul(localNormal, (float3x3)world));
    float canopyHeight = saturate((treePosition.y - CrownProfile.x) / (2 * (CrownProfile.y - CrownProfile.x)));
    float cluster = 1 - abs(frac(dot(treePosition, float3(1.22, 1.08, 0.94))) * 2 - 1);
    float canopyShade = lerp(0.72, 1.12, canopyHeight) + (cluster - 0.5) * 0.14;
    output.Position = mul(p, ViewProjection);
    output.UV = input.UV;
    output.Lighting = AmbientColor + max(0, dot(normal, -normalize(LightDirection))) * float3(0.38, 0.35, 0.29);
    output.Lighting *= lerp(1, canopyShade, LeafColor.a);
    return output;
}
float4 InstancedPS(PixelInput input) : COLOR0
{
    float4 color = tex2D(PaletteSampler, input.UV);
    clip(color.a - 0.3);
    float3 albedo = lerp(color.rgb, LeafColor.rgb, LeafColor.a);
    return float4(albedo * input.Lighting * Tint, Opacity);
}
technique Instanced
{
    pass P0
    {
        VertexShader = compile VS_MODEL InstancedVS();
        PixelShader = compile PS_MODEL InstancedPS();
    }
}
