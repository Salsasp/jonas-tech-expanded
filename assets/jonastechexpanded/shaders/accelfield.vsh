#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

// Temporal acceleration field sphere. The mesh is a unit sphere; modelViewMatrix places and scales it.
// Same vertex layout as vanilla's rift shader: position at 0, uv at 1.

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 uvIn;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

// Position on the unit sphere, which is also its outward normal in model space.
out vec3 localPos;
// Eye-space position and normal, for the rim (fresnel) term.
out vec3 viewPos;
out vec3 viewNormal;
out vec2 uv;

void main()
{
	localPos = vertexPosition;
	uv = uvIn;

	vec4 eye = modelViewMatrix * vec4(vertexPosition, 1.0);
	viewPos = eye.xyz;
	// Uniform scale only, so the model-view matrix itself transforms normals correctly.
	viewNormal = normalize((modelViewMatrix * vec4(vertexPosition, 0.0)).xyz);

	gl_Position = projectionMatrix * eye;
}
