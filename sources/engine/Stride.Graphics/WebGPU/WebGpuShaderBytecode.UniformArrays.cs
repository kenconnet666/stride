// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_GRAPHICS_API_WEBGPU
using System;
using System.Collections.Generic;

namespace Stride.Graphics;

internal static partial class WebGpuShaderBytecode
{
    // A constant buffer lays out each array element on 16 bytes. WGSL derives the layout from the types alone: an array
    // of scalars or of two-component vectors would get a stride of 4 or 8 bytes. Such an element is widened to a
    // four-component vector, which takes 16 bytes, and each read takes the original components out of it.
    private static List<uint> WidenUniformArrayElements(List<uint> source)
    {
        const uint ElementStride = 16;
        var scalars = new HashSet<uint>();
        var vectors = new Dictionary<uint, (uint Component, uint Count)>();
        var matrices = new Dictionary<uint, uint>();
        var arrays = new Dictionary<uint, uint>();
        var structures = new Dictionary<uint, uint[]>();
        var pointers = new Dictionary<uint, (uint Storage, uint Type)>();
        var constants = new Dictionary<uint, uint>();
        var strides = new Dictionary<uint, uint>();
        uint indexType = 0;
        for (var i = 5; i < source.Count; i += (int)(source[i] >> 16))
        {
            var count = (int)(source[i] >> 16);
            switch (source[i] & 0xffff)
            {
                case 71 when count == 4 && source[i + 2] == 6: strides[source[i + 1]] = source[i + 3]; break; // ArrayStride
                case 21 when source[i + 2] == 32: // OpTypeInt
                    scalars.Add(source[i + 1]);
                    if (indexType == 0) indexType = source[i + 1];
                    break;
                case 22 when source[i + 2] == 32: scalars.Add(source[i + 1]); break; // OpTypeFloat
                case 23: vectors[source[i + 1]] = (source[i + 2], source[i + 3]); break;
                case 24: matrices[source[i + 1]] = source[i + 2]; break;
                case 28 or 29: arrays[source[i + 1]] = source[i + 2]; break; // OpTypeArray, OpTypeRuntimeArray
                case 30: structures[source[i + 1]] = source.GetRange(i + 2, count - 2).ToArray(); break;
                case 32: pointers[source[i + 1]] = (source[i + 2], source[i + 3]); break;
                case 43 when count == 4: constants[source[i + 2]] = source[i + 3]; break;
            }
        }

        var nextId = source[3];
        var declarations = new List<uint>();
        var wideVectors = new Dictionary<uint, uint>();
        uint WideVector(uint component)
        {
            if (wideVectors.TryGetValue(component, out var wide)) return wide;
            foreach (var (id, vector) in vectors)
                if (vector == (component, 4u)) return wideVectors[component] = id;
            wide = nextId++;
            vectors[wide] = (component, 4);
            declarations.AddRange([(4u << 16) | 23, wide, component, 4]);
            return wideVectors[component] = wide;
        }

        // The array type, to the type that takes the place of its element
        var widened = new Dictionary<uint, uint>();
        foreach (var (array, stride) in strides)
        {
            if (stride != ElementStride || !arrays.TryGetValue(array, out var element)) continue;
            if (scalars.Contains(element)) widened[array] = WideVector(element);
            else if (vectors.TryGetValue(element, out var vector) && vector.Count == 2 && scalars.Contains(vector.Component)) widened[array] = WideVector(vector.Component);
        }
        if (widened.Count == 0) return source;

        var zero = nextId++;
        declarations.AddRange([(4u << 16) | 43, indexType, zero, 0]);
        // A pointer to an element that was a two-component vector points to the wide vector
        var widePointers = new Dictionary<uint, uint>();
        var pointerDeclarations = new List<uint>();
        foreach (var (id, pointer) in new List<KeyValuePair<uint, (uint Storage, uint Type)>>(pointers))
        {
            if (!vectors.TryGetValue(pointer.Type, out var vector) || vector.Count != 2 || !wideVectors.TryGetValue(vector.Component, out var wide)) continue;
            var widePointer = nextId++;
            pointers[widePointer] = (pointer.Storage, wide);
            pointerDeclarations.AddRange([(4u << 16) | 32, widePointer, pointer.Storage, wide]);
            widePointers[id] = widePointer;
        }

        var result = source.GetRange(0, 5);
        var valueTypes = new Dictionary<uint, uint>();
        // The pointers to a widened element, to the type the shader reads from them
        var narrowTypes = new Dictionary<uint, uint>();
        var declared = false;
        void RequireRead(uint id)
        {
            if (narrowTypes.ContainsKey(id))
                throw new NotSupportedException("WebGPU only reads the elements of a constant buffer array of two-component vectors.");
        }
        for (var i = 5; i < source.Count; i += (int)(source[i] >> 16))
        {
            var count = (int)(source[i] >> 16);
            var instruction = source.GetRange(i, count);
            switch (instruction[0] & 0xffff)
            {
                case 28 when widened.TryGetValue(instruction[1], out var wide):
                    if (!declared) { result.AddRange(declarations); declared = true; }
                    instruction[2] = wide;
                    break;
                case 54 when pointerDeclarations.Count > 0: // OpFunction: the types are all declared
                    result.AddRange(pointerDeclarations);
                    pointerDeclarations.Clear();
                    break;
                case 55 or 59: valueTypes[instruction[2]] = instruction[1]; break; // OpFunctionParameter, OpVariable
                case 44 or 80 when widened.ContainsKey(instruction[1]): // OpConstantComposite, OpCompositeConstruct
                    throw new NotSupportedException("WebGPU cannot build a value of a constant buffer array of scalars or two-component vectors.");
                case 62 or 83: RequireRead(instruction[(instruction[0] & 0xffff) == 62 ? 1 : 3]); break; // OpStore, OpCopyObject
                case 57: // OpFunctionCall
                    for (var argument = 4; argument < count; argument++) RequireRead(instruction[argument]);
                    break;
                case 61 when narrowTypes.TryGetValue(instruction[3], out var narrow): // OpLoad
                    var loaded = nextId++;
                    result.AddRange([(4u << 16) | 61, pointers[valueTypes[instruction[3]]].Type, loaded, instruction[3]]);
                    result.AddRange([(7u << 16) | 79, narrow, instruction[2], loaded, loaded, 0, 1]); // OpVectorShuffle
                    continue;
                case 65 or 66: // OpAccessChain, OpInBoundsAccessChain
                    valueTypes[instruction[2]] = instruction[1];
                    if (!valueTypes.TryGetValue(instruction[3], out var pointer) || !pointers.TryGetValue(pointer, out var pointee)) break;
                    var type = pointee.Type;
                    for (var index = 4; index < instruction.Count; index++)
                    {
                        if (structures.TryGetValue(type, out var memberTypes))
                        {
                            type = memberTypes[constants[instruction[index]]];
                        }
                        else if (arrays.TryGetValue(type, out var element))
                        {
                            if (widened.ContainsKey(type))
                            {
                                // A scalar is the first component of the wide vector
                                if (scalars.Contains(element)) instruction.Insert(++index, zero);
                                else if (index == instruction.Count - 1)
                                {
                                    narrowTypes[instruction[2]] = element;
                                    valueTypes[instruction[2]] = instruction[1] = widePointers[instruction[1]];
                                }
                            }
                            type = element;
                        }
                        else if (matrices.TryGetValue(type, out var column)) type = column;
                        else if (vectors.TryGetValue(type, out var vector)) type = vector.Component;
                        else break;
                    }
                    instruction[0] = ((uint)instruction.Count << 16) | (instruction[0] & 0xffff);
                    break;
            }
            result.AddRange(instruction);
        }
        result[3] = nextId;
        return result;
    }
}
#endif
