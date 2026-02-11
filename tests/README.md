# MeshThicknessAnalyzer Tests

Comprehensive unit tests for the `MeshThicknessAnalyzer` class in geometry3Sharp.

## Overview

This test suite validates the wall thickness analysis functionality for dental CAD/CAM meshes. The `MeshThicknessAnalyzer` class analyzes minimum wall thickness by ray-casting inward from each vertex along inverted normals.

## Test Coverage

The test suite includes **14 comprehensive test cases**:

### Basic Functionality Tests
1. **TestUniformSphereMesh** - Validates uniform thickness on a perfect sphere
2. **TestBoxMeshWithKnownThickness** - Tests known thickness values on box geometry
3. **TestThinRegionDetection** - Verifies detection of thin areas below threshold
4. **TestThresholdConfiguration** - Confirms threshold changes affect detection

### Edge Cases & Robustness
5. **TestEmptyMesh** - Empty mesh with zero vertices
6. **TestSingleTriangleMesh** - Minimal valid mesh
7. **TestDegenerateMesh** - Collapsed/degenerate triangles
8. **TestOpenMeshWithBoundaries** - Non-watertight meshes

### Normal Orientation Tests
9. **TestNormalOrientationInward** - Inward-pointing normals
10. **TestNormalOrientationOutward** - Standard outward normals

### Stress Tests
11. **TestLargeThicknessValues** - Very thick geometry (1000mm radius)
12. **TestVeryThinRegions** - Extremely thin walls (0.01mm)
13. **TestMeshWithNoHits** - Rays that don't intersect geometry
14. **TestPerformanceWithLargeMesh** - Higher vertex count performance

## Running the Tests

### Prerequisites
- .NET 6.0, .NET 5.0, or .NET Core 3.1 SDK installed
- geometry3Sharp library built

### Build and Run

From the project root directory:

```bash
# Build the test project
dotnet build tests/MeshThicknessAnalyzerTests.csproj

# Run the tests
dotnet run --project tests/MeshThicknessAnalyzerTests.csproj
```

Or from the tests directory:

```bash
cd tests
dotnet build
dotnet run
```

### Expected Output

```
==========================================
MeshThicknessAnalyzer Test Suite
==========================================

✓ PASS: TestUniformSphereMesh
✓ PASS: TestBoxMeshWithKnownThickness
✓ PASS: TestThinRegionDetection
...

==========================================
Test Results Summary
==========================================
Total Tests:  14
Passed:       14 ✓
Failed:       0 ✗
Success Rate: 100.0%
==========================================
```

## Test Implementation Details

### MeshThicknessAnalyzer Class

The class under test should implement:

```csharp
public class MeshThicknessAnalyzer
{
    public MeshThicknessAnalyzer(DMesh3 mesh, DMeshAABBTree3 spatial, double thicknessThreshold = 0.5)

    public ThicknessAnalysisResult Compute()
}

public class ThicknessAnalysisResult
{
    public Dictionary<int, double> VertexThickness { get; set; }
    public List<int> ThinVertices { get; set; }
}
```

### Algorithm

For each vertex in the mesh:
1. Get vertex position and normal vector
2. Create a ray from the vertex along the **inverted normal** direction
3. Use `DMeshAABBTree3.FindNearestHitTriangle(ray)` to find intersection
4. Calculate distance to hit point (wall thickness)
5. Store thickness value in result dictionary
6. If thickness < threshold (default 0.5mm), add to thin vertices list

### Test Mesh Generators Used

- `Sphere3Generator_NormalizedCube` - Generates sphere meshes
- `TrivialBox3Generator` - Generates simple box meshes
- `GridBox3Generator` - Generates subdivided box meshes
- Manual `DMesh3` construction for custom test geometries

## File Structure

```
tests/
├── MeshThicknessAnalyzerTests.cs      # Test cases and analyzer stub
├── MeshThicknessAnalyzerTests.csproj  # Test project configuration
└── README.md                          # This file
```

## Notes

- The test file currently includes a **placeholder implementation** of `MeshThicknessAnalyzer` and `ThicknessAnalysisResult` classes
- These should be replaced with your actual implementation
- All tests are designed to be **independent** and can run in any order
- Tests use manual assertions (no external test framework required)
- Exit code: 0 = all passed, 1 = one or more failures

## Future Enhancements

Potential additional test cases:
- Concave geometry with complex ray intersections
- Non-manifold mesh handling
- Multi-threaded performance testing
- Memory usage profiling
- Visualization of thin regions
- Integration with actual dental mesh samples

## Author

Created as part of geometry3Sharp dental CAD/CAM demonstration.
