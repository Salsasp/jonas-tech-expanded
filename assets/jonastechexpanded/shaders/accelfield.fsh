#version 330 core

// Temporal acceleration field sphere: a translucent, shimmering force field. Drawn after the scene is blitted
// (like vanilla's rift), so it hides itself behind terrain by reading the scene depth texture, and glows where it
// cuts through blocks.

in vec3 localPos;
in vec3 viewPos;
in vec3 viewNormal;
in vec2 uv;

out vec4 outColor;

uniform mat4 projectionMatrix;
uniform sampler2D depthTex;
uniform vec2 invFrameSize;

uniform float time;
// 0..1, fades the whole field in and out.
uniform float intensity;
uniform vec3 fieldColor;
// Sphere radius in blocks, so patterns keep a similar on-screen size whatever the field size.
uniform float radius;

#include noise3d.ash

vec3 hash3(vec3 p)
{
	p = vec3(dot(p, vec3(127.1, 311.7, 74.7)), dot(p, vec3(269.5, 183.3, 246.1)), dot(p, vec3(113.5, 271.9, 124.6)));
	return fract(sin(p) * 43758.5453123);
}

// Distance to the nearest cell border of a 3D Voronoi lattice whose points drift over time.
// Seam-free on a sphere, unlike a 2D hex grid wrapped around it.
float cellEdge(vec3 p, float t)
{
	vec3 cell = floor(p);
	vec3 frac = fract(p);
	float f1 = 8.0;
	float f2 = 8.0;

	for (int x = -1; x <= 1; x++)
	for (int y = -1; y <= 1; y++)
	for (int z = -1; z <= 1; z++)
	{
		vec3 offset = vec3(x, y, z);
		vec3 h = hash3(cell + offset);
		vec3 point = offset + 0.5 + 0.35 * sin(t + 6.2831 * h);
		float d = length(point - frac);
		if (d < f1) { f2 = f1; f1 = d; }
		else if (d < f2) { f2 = d; }
	}

	return f2 - f1;
}

// Eye-space distance of a depth-buffer sample, exact for a perspective projection.
float eyeDistance(float depth)
{
	float ndc = depth * 2.0 - 1.0;
	return projectionMatrix[3][2] / (ndc + projectionMatrix[2][2]);
}

void main()
{
	vec2 screenUv = gl_FragCoord.xy * invFrameSize;
	float sceneDepth = texture(depthTex, screenUv).r;

	// Behind terrain: hidden.
	if (gl_FragCoord.z > sceneDepth) discard;

	float fragDist = -viewPos.z;
	float sceneDist = eyeDistance(sceneDepth);
	// 1 where the sphere surface touches a block, fading over about half a block.
	float intersect = 1.0 - clamp((sceneDist - fragDist) / 0.5, 0.0, 1.0);

	vec3 n = normalize(viewNormal);
	vec3 v = normalize(-viewPos);
	float rim = pow(1.0 - abs(dot(n, v)), 2.5);

	// Surface position in blocks, so pattern density doesn't depend on the field size.
	vec3 p = localPos * radius;

	// Slow, swirling energy flow.
	float flow = cnoise(p * 0.45 + vec3(0.0, -time * 0.35, time * 0.2)) * 0.5 + 0.5;

	// Drifting cell lattice, brightest on the borders, its lines breathing with the flow.
	float edge = cellEdge(p * 0.9, time * 0.6);
	float lattice = 1.0 - smoothstep(0.02, 0.09 + flow * 0.05, edge);

	// Bands of light sweeping down the field.
	float band = sin(localPos.y * 7.0 + time * 1.8 + flow * 2.5);
	float pulse = smoothstep(0.85, 1.0, band);

	// Faint high-frequency shimmer and an occasional whole-field flicker.
	float shimmer = cnoise(p * 3.0 + vec3(time * 1.5)) * 0.5 + 0.5;
	float flicker = 0.85 + 0.15 * smoothstep(0.3, 0.8, cnoise(vec3(time * 2.3, 0.0, 0.0)) * 0.5 + 0.5);

	float alpha =
		0.04
		+ rim * 0.45
		+ lattice * (0.18 + flow * 0.22)
		+ pulse * 0.18
		+ shimmer * 0.04
		+ intersect * 0.65;

	alpha *= flicker * intensity;
	if (alpha < 0.005) discard;

	vec3 color = fieldColor * (0.55 + flow * 0.45);
	// Hot spots (lattice, pulses, intersections) wash out towards white like an energy surface.
	color = mix(color, vec3(0.85, 1.0, 1.0), clamp(lattice * 0.35 + pulse * 0.4 + intersect * 0.6, 0.0, 1.0));

	outColor = vec4(color, clamp(alpha, 0.0, 0.9));
}
