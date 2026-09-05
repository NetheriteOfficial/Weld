namespace Weld.Carisle.Rendering;

internal static class Shaders
{
    public const string SceneVertex = """
        #version 450 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;

        uniform mat4 uModel;
        uniform mat4 uViewProj;

        out vec3 vWorldPos;
        out vec3 vNormal;
        out vec2 vUv;

        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorldPos = world.xyz;
            vNormal = mat3(uModel) * aNormal;
            vUv = aUv;
            gl_Position = uViewProj * world;
        }
        """;

    public const string SceneFragment = """
        #version 450 core
        in vec3 vWorldPos;
        in vec3 vNormal;
        in vec2 vUv;

        uniform sampler2D uTexture;
        uniform int  uHasTexture;
        uniform vec4 uDiffuse;
        uniform vec3 uLightDir;
        uniform vec3 uFogColor;
        uniform vec2 uFogRange;
        uniform vec3 uCameraPos;
        uniform float uOpacity;
        uniform int   uDitherInvert;

        out vec4 fragColor;

        float bayer4(ivec2 p)
        {
            const float m[16] = float[16](
                 0.0,  8.0,  2.0, 10.0,
                12.0,  4.0, 14.0,  6.0,
                 3.0, 11.0,  1.0,  9.0,
                15.0,  7.0, 13.0,  5.0);
            return (m[(p.y & 3) * 4 + (p.x & 3)] + 0.5) / 16.0;
        }

        void main()
        {
            float threshold = bayer4(ivec2(gl_FragCoord.xy));
            if (uDitherInvert == 1) threshold = 1.0 - threshold;
            if (uOpacity < threshold) discard;

            vec4 albedo = uDiffuse;
            if (uHasTexture == 1) albedo *= texture(uTexture, vUv);
            if (albedo.a < 0.5) discard;

            vec3 n = normalize(vNormal);
            float ndl = max(dot(n, normalize(uLightDir)), 0.0);
            float light = 0.35 + 0.65 * ndl;
            vec3 color = albedo.rgb * light;

            float dist = distance(vWorldPos, uCameraPos);
            float fog = clamp((dist - uFogRange.x) / max(uFogRange.y - uFogRange.x, 0.001), 0.0, 1.0);
            color = mix(color, uFogColor, fog);

            fragColor = vec4(color, 1.0);
        }
        """;

    public const string UpscaleVertex = """
        #version 450 core
        uniform vec4 uScaleOffset;
        out vec2 vUv;
        void main()
        {
            vec2 uv = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vec2 ndc = uv * 2.0 - 1.0;
            vUv = uv;
            gl_Position = vec4(ndc * uScaleOffset.xy + uScaleOffset.zw, 0.0, 1.0);
        }
        """;

    public const string UpscaleFragment = """
        #version 450 core
        in vec2 vUv;
        uniform sampler2D uSource;
        uniform vec2 uSourceSize;
        uniform vec2 uOutputSize;
        uniform int  uFilter;
        out vec4 fragColor;

        vec4 textureBilinear(vec2 uv);

        vec2 sharpUv(vec2 uv)
        {
            vec2 texel = uv * uSourceSize;
            vec2 scale = max(floor(uOutputSize / uSourceSize), vec2(1.0));
            vec2 texelFloored = floor(texel);
            vec2 s = fract(texel);
            vec2 regionRange = 0.5 - 0.5 / scale;
            vec2 centerDist = s - 0.5;
            vec2 f = (centerDist - clamp(centerDist, -regionRange, regionRange)) * scale + 0.5;
            return (texelFloored + f) / uSourceSize;
        }

        void main()
        {
            if (vUv.x < 0.0 || vUv.x > 1.0 || vUv.y < 0.0 || vUv.y > 1.0) discard;
            vec2 uv = vUv;
            if (uFilter == 0)
            {
                uv = (floor(uv * uSourceSize) + 0.5) / uSourceSize;
                fragColor = texture(uSource, uv);
            }
            else if (uFilter == 1)
            {
                fragColor = textureBilinear(uv);
            }
            else
            {
                fragColor = textureBilinear(sharpUv(uv));
            }
        }

        vec4 textureBilinear(vec2 uv)
        {
            vec2 texel = uv * uSourceSize - 0.5;
            vec2 i = floor(texel);
            vec2 f = fract(texel);
            vec2 inv = 1.0 / uSourceSize;
            vec4 a = texture(uSource, (i + vec2(0.5, 0.5)) * inv);
            vec4 b = texture(uSource, (i + vec2(1.5, 0.5)) * inv);
            vec4 c = texture(uSource, (i + vec2(0.5, 1.5)) * inv);
            vec4 d = texture(uSource, (i + vec2(1.5, 1.5)) * inv);
            return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
        }
        """;
}
