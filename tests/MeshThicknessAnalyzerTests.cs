using System;
using System.Linq;
using g3;

namespace g3.tests
{
    /// <summary>
    /// Comprehensive unit tests for MeshThicknessAnalyzer class.
    /// This is a standalone test class that can be run independently.
    ///
    /// Run with: dotnet run --project tests/MeshThicknessAnalyzerTests.csproj
    /// Or compile and run the executable directly.
    /// </summary>
    public class MeshThicknessAnalyzerTests
    {
        private static int testsPassed = 0;
        private static int testsFailed = 0;
        private static int testsTotal = 0;

        public static void Main(string[] args)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("MeshThicknessAnalyzer Test Suite");
            Console.WriteLine("==========================================\n");

            // Run all test cases
            TestUniformSphereMesh();
            TestBoxMeshWithKnownThickness();
            TestThinRegionDetection();
            TestThresholdConfiguration();
            TestEmptyMesh();
            TestSingleTriangleMesh();
            TestDegenerateMesh();
            TestOpenMeshWithBoundaries();
            TestNormalOrientationInward();
            TestNormalOrientationOutward();
            TestLargeThicknessValues();
            TestVeryThinRegions();
            TestMeshWithNoHits();
            TestPerformanceWithLargeMesh();

            // Print results
            Console.WriteLine("\n==========================================");
            Console.WriteLine("Test Results Summary");
            Console.WriteLine("==========================================");
            Console.WriteLine($"Total Tests:  {testsTotal}");
            Console.WriteLine($"Passed:       {testsPassed} ✓");
            Console.WriteLine($"Failed:       {testsFailed} ✗");
            Console.WriteLine($"Success Rate: {(testsTotal > 0 ? (testsPassed * 100.0 / testsTotal) : 0):F1}%");
            Console.WriteLine("==========================================\n");

            Environment.Exit(testsFailed > 0 ? 1 : 0);
        }

        #region Test Cases

        /// <summary>
        /// Test 1: Uniform sphere mesh
        /// A perfect sphere should have relatively uniform thickness values across all vertices.
        /// Expected: All thickness values should be approximately 2*radius (diameter).
        /// </summary>
        private static void TestUniformSphereMesh()
        {
            string testName = "TestUniformSphereMesh";
            try
            {
                // Arrange
                double radius = 10.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 8,
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                AssertTrue(result.VertexThickness.Count == mesh.VertexCount,
                    $"Should have thickness for all {mesh.VertexCount} vertices", testName);

                // For a sphere, thickness should be approximately 2*radius
                // Allow some tolerance for numerical precision and ray-casting discretization
                double expectedThickness = 2.0 * radius;
                double tolerance = radius * 0.3; // 30% tolerance for sphere approximation

                int validThicknessCount = 0;
                foreach (var thickness in result.VertexThickness.Values)
                {
                    if (thickness > 0 && Math.Abs(thickness - expectedThickness) < tolerance)
                    {
                        validThicknessCount++;
                    }
                }

                double validPercentage = (validThicknessCount * 100.0) / mesh.VertexCount;
                AssertTrue(validPercentage > 70,
                    $"At least 70% of vertices should have thickness near {expectedThickness:F2} (got {validPercentage:F1}%)",
                    testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 2: Box mesh with known wall thickness
        /// A hollow box or solid box should return known thickness values.
        /// </summary>
        private static void TestBoxMeshWithKnownThickness()
        {
            string testName = "TestBoxMeshWithKnownThickness";
            try
            {
                // Arrange
                double sideLength = 20.0;
                var boxGen = new TrivialBox3Generator
                {
                    Box = new Box3d(Vector3d.Zero, new Vector3d(sideLength / 2.0, sideLength / 2.0, sideLength / 2.0)),
                    NoSharedVertices = true
                };
                boxGen.Generate();
                DMesh3 mesh = boxGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                AssertTrue(result.VertexThickness.Count == mesh.VertexCount,
                    "Should have thickness for all vertices", testName);

                // For a box, thickness should be the distance across
                // This will be sideLength for most vertices
                double expectedThickness = sideLength;
                double tolerance = sideLength * 0.2;

                int validThicknessCount = 0;
                foreach (var thickness in result.VertexThickness.Values)
                {
                    if (thickness > 0 && Math.Abs(thickness - expectedThickness) < tolerance)
                    {
                        validThicknessCount++;
                    }
                }

                AssertTrue(validThicknessCount > 0,
                    "At least some vertices should have valid thickness measurements", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 3: Thin region detection
        /// Create a mesh with a known thin region and verify it's detected.
        /// </summary>
        private static void TestThinRegionDetection()
        {
            string testName = "TestThinRegionDetection";
            try
            {
                // Arrange - Create two parallel planes close together
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                double separation = 0.3; // Thin gap - below default threshold of 0.5

                // Bottom plane (normal pointing up)
                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-5, 0, -5), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, 0, -5), n = Vector3f.AxisY });
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, 0, 5), n = Vector3f.AxisY });
                int v3 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-5, 0, 5), n = Vector3f.AxisY });

                // Top plane (normal pointing down)
                int v4 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-5, separation, -5), n = -Vector3f.AxisY });
                int v5 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, separation, -5), n = -Vector3f.AxisY });
                int v6 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, separation, 5), n = -Vector3f.AxisY });
                int v7 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-5, separation, 5), n = -Vector3f.AxisY });

                // Bottom plane triangles
                mesh.AppendTriangle(v0, v1, v2);
                mesh.AppendTriangle(v0, v2, v3);

                // Top plane triangles
                mesh.AppendTriangle(v4, v6, v5);
                mesh.AppendTriangle(v4, v7, v6);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                AssertTrue(result.ThinVertices.Count > 0,
                    "Should detect thin vertices", testName);

                // All vertices should be flagged as thin (below 0.5mm threshold)
                AssertTrue(result.ThinVertices.Count == mesh.VertexCount,
                    $"All {mesh.VertexCount} vertices should be flagged as thin (separation={separation})", testName);

                // Check that measured thickness is approximately correct
                var thicknesses = result.VertexThickness.Values.Where(t => t > 0).ToList();
                if (thicknesses.Count > 0)
                {
                    double avgThickness = thicknesses.Average();
                    AssertTrue(Math.Abs(avgThickness - separation) < 0.15,
                        $"Average thickness should be near {separation} (got {avgThickness:F3})", testName);
                }

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 4: Threshold configuration
        /// Changing the threshold should change which vertices are flagged as thin.
        /// </summary>
        private static void TestThresholdConfiguration()
        {
            string testName = "TestThresholdConfiguration";
            try
            {
                // Arrange
                double radius = 5.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 6,
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);

                // Act - Run with very high threshold (should flag many/all vertices)
                var analyzerHigh = new MeshThicknessAnalyzer(mesh, spatial, thicknessThreshold: 100.0);
                var resultHigh = analyzerHigh.Compute();

                // Run with very low threshold (should flag few/no vertices)
                var analyzerLow = new MeshThicknessAnalyzer(mesh, spatial, thicknessThreshold: 0.01);
                var resultLow = analyzerLow.Compute();

                // Assert
                AssertNotNull(resultHigh, "High threshold result should not be null", testName);
                AssertNotNull(resultLow, "Low threshold result should not be null", testName);

                // High threshold should flag more vertices than low threshold
                AssertTrue(resultHigh.ThinVertices.Count > resultLow.ThinVertices.Count,
                    $"High threshold ({resultHigh.ThinVertices.Count}) should flag more vertices than low threshold ({resultLow.ThinVertices.Count})",
                    testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 5: Empty mesh (zero vertices)
        /// Should handle gracefully without crashing.
        /// </summary>
        private static void TestEmptyMesh()
        {
            string testName = "TestEmptyMesh";
            try
            {
                // Arrange
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);
                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null even for empty mesh", testName);
                AssertTrue(result.VertexThickness.Count == 0, "Empty mesh should have zero thickness values", testName);
                AssertTrue(result.ThinVertices.Count == 0, "Empty mesh should have zero thin vertices", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 6: Single triangle mesh
        /// Minimal valid mesh - should handle without error.
        /// </summary>
        private static void TestSingleTriangleMesh()
        {
            string testName = "TestSingleTriangleMesh";
            try
            {
                // Arrange
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(1, 0, 0), n = Vector3f.AxisY });
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 1), n = Vector3f.AxisY });
                mesh.AppendTriangle(v0, v1, v2);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                AssertTrue(result.VertexThickness.Count == 3, "Should have 3 vertex thickness values", testName);

                // Single triangle - rays will likely not hit anything (open mesh)
                // This is expected behavior
                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 7: Degenerate mesh (collapsed triangles)
        /// Test robustness with degenerate geometry.
        /// </summary>
        private static void TestDegenerateMesh()
        {
            string testName = "TestDegenerateMesh";
            try
            {
                // Arrange - Create triangles with coincident vertices
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 0), n = Vector3f.AxisY }); // Same position
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(1, 0, 0), n = Vector3f.AxisY });

                mesh.AppendTriangle(v0, v1, v2); // Degenerate triangle

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act - Should not crash
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Should handle degenerate mesh without crashing", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 8: Open mesh with boundary edges
        /// Mesh with boundaries where rays might not hit anything.
        /// </summary>
        private static void TestOpenMeshWithBoundaries()
        {
            string testName = "TestOpenMeshWithBoundaries";
            try
            {
                // Arrange - Create a simple open quad (not closed)
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, 0, 0), n = Vector3f.AxisY });
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(5, 0, 5), n = Vector3f.AxisY });
                int v3 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(0, 0, 5), n = Vector3f.AxisY });

                mesh.AppendTriangle(v0, v1, v2);
                mesh.AppendTriangle(v0, v2, v3);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                AssertTrue(result.VertexThickness.Count == mesh.VertexCount,
                    "Should process all vertices even in open mesh", testName);

                // In an open mesh, many vertices might have zero thickness (no hit)
                // This is expected behavior
                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 9: Normal orientation - inward normals
        /// Test with normals pointing inward (should still work correctly).
        /// </summary>
        private static void TestNormalOrientationInward()
        {
            string testName = "TestNormalOrientationInward";
            try
            {
                // Arrange - Create sphere with inward-pointing normals
                double radius = 8.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 6,
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                // Flip all normals to point inward
                foreach (int vid in mesh.VertexIndices())
                {
                    Vector3f normal = mesh.GetVertexNormal(vid);
                    mesh.SetVertexNormal(vid, -normal);
                }

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null with inward normals", testName);
                AssertTrue(result.VertexThickness.Count > 0, "Should compute thickness with inward normals", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 10: Normal orientation - outward normals (standard case)
        /// Verify correct behavior with standard outward normals.
        /// </summary>
        private static void TestNormalOrientationOutward()
        {
            string testName = "TestNormalOrientationOutward";
            try
            {
                // Arrange - Standard sphere with outward normals
                double radius = 8.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 6,
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null with outward normals", testName);
                AssertTrue(result.VertexThickness.Count > 0, "Should compute thickness with outward normals", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 11: Large thickness values
        /// Test with very thick mesh to ensure no overflow or precision issues.
        /// </summary>
        private static void TestLargeThicknessValues()
        {
            string testName = "TestLargeThicknessValues";
            try
            {
                // Arrange - Large sphere
                double radius = 1000.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 5,
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial, thicknessThreshold: 100.0);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null with large mesh", testName);

                // Verify no NaN or infinite values
                foreach (var thickness in result.VertexThickness.Values)
                {
                    AssertTrue(!double.IsNaN(thickness) && !double.IsInfinity(thickness),
                        "Thickness values should not be NaN or Infinity", testName);
                }

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 12: Very thin regions (stress test)
        /// Test with extremely thin walls to verify precision.
        /// </summary>
        private static void TestVeryThinRegions()
        {
            string testName = "TestVeryThinRegions";
            try
            {
                // Arrange - Two planes very close together
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                double separation = 0.01; // Very thin - 0.01mm

                // Bottom plane
                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-3, 0, -3), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(3, 0, -3), n = Vector3f.AxisY });
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(3, 0, 3), n = Vector3f.AxisY });
                int v3 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-3, 0, 3), n = Vector3f.AxisY });

                // Top plane
                int v4 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-3, separation, -3), n = -Vector3f.AxisY });
                int v5 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(3, separation, -3), n = -Vector3f.AxisY });
                int v6 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(3, separation, 3), n = -Vector3f.AxisY });
                int v7 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-3, separation, 3), n = -Vector3f.AxisY });

                mesh.AppendTriangle(v0, v1, v2);
                mesh.AppendTriangle(v0, v2, v3);
                mesh.AppendTriangle(v4, v6, v5);
                mesh.AppendTriangle(v4, v7, v6);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial, thicknessThreshold: 0.5);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null with very thin walls", testName);
                AssertTrue(result.ThinVertices.Count == mesh.VertexCount,
                    "All vertices should be flagged as thin", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 13: Mesh where rays don't hit anything
        /// Test case where inverted normals point away from mesh.
        /// </summary>
        private static void TestMeshWithNoHits()
        {
            string testName = "TestMeshWithNoHits";
            try
            {
                // Arrange - Single sided plane where inverted normals point away
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                int v0 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-2, 0, -2), n = Vector3f.AxisY });
                int v1 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(2, 0, -2), n = Vector3f.AxisY });
                int v2 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(2, 0, 2), n = Vector3f.AxisY });
                int v3 = mesh.AppendVertex(new NewVertexInfo { v = new Vector3d(-2, 0, 2), n = Vector3f.AxisY });

                mesh.AppendTriangle(v0, v1, v2);
                mesh.AppendTriangle(v0, v2, v3);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var result = analyzer.Compute();

                // Assert
                AssertNotNull(result, "Result should not be null even with no hits", testName);

                // Vertices might have zero thickness if rays don't hit anything
                // This is valid behavior - analyzer should handle gracefully
                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        /// <summary>
        /// Test 14: Performance test with larger mesh
        /// Ensure analyzer can handle meshes with more vertices efficiently.
        /// </summary>
        private static void TestPerformanceWithLargeMesh()
        {
            string testName = "TestPerformanceWithLargeMesh";
            try
            {
                // Arrange - Higher resolution sphere
                double radius = 15.0;
                var sphereGen = new Sphere3Generator_NormalizedCube
                {
                    EdgeVertices = 20, // Creates a denser mesh
                    Radius = radius,
                    Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
                };
                sphereGen.Generate();
                DMesh3 mesh = sphereGen.MakeDMesh();

                Console.WriteLine($"  (Testing with {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles)");

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                // Act
                var startTime = DateTime.Now;
                var result = analyzer.Compute();
                var elapsed = DateTime.Now - startTime;

                // Assert
                AssertNotNull(result, "Result should not be null", testName);
                Console.WriteLine($"  Computation time: {elapsed.TotalMilliseconds:F0}ms");

                // Should complete in reasonable time (< 30 seconds even for large mesh)
                AssertTrue(elapsed.TotalSeconds < 30,
                    $"Should complete in < 30s (took {elapsed.TotalSeconds:F1}s)", testName);

                PassTest(testName);
            }
            catch (Exception ex)
            {
                FailTest(testName, ex.Message);
            }
        }

        #endregion

        #region Test Helper Methods

        private static void AssertTrue(bool condition, string message, string testName)
        {
            if (!condition)
            {
                throw new Exception($"Assertion failed: {message}");
            }
        }

        private static void AssertNotNull(object obj, string message, string testName)
        {
            if (obj == null)
            {
                throw new Exception($"Assertion failed: {message}");
            }
        }

        private static void PassTest(string testName)
        {
            testsPassed++;
            testsTotal++;
            Console.WriteLine($"✓ PASS: {testName}");
        }

        private static void FailTest(string testName, string error)
        {
            testsFailed++;
            testsTotal++;
            Console.WriteLine($"✗ FAIL: {testName}");
            Console.WriteLine($"  Error: {error}");
        }

        #endregion
    }

    #region MeshThicknessAnalyzer - CLASS UNDER TEST (Placeholder)

    /// <summary>
    /// Analyzes wall thickness on a mesh by ray-casting inward from each vertex along inverted normal.
    /// NOTE: This is a placeholder/stub - the actual implementation should be provided.
    /// </summary>
    public class MeshThicknessAnalyzer
    {
        private readonly DMesh3 mesh;
        private readonly DMeshAABBTree3 spatial;
        private readonly double thicknessThreshold;

        public MeshThicknessAnalyzer(DMesh3 mesh, DMeshAABBTree3 spatial, double thicknessThreshold = 0.5)
        {
            this.mesh = mesh;
            this.spatial = spatial;
            this.thicknessThreshold = thicknessThreshold;
        }

        public ThicknessAnalysisResult Compute()
        {
            var result = new ThicknessAnalysisResult();

            // TODO: Implement actual thickness analysis
            // For each vertex:
            //   1. Get vertex position and normal
            //   2. Create ray from vertex along inverted normal
            //   3. Find nearest hit using spatial.FindNearestHitTriangle(ray)
            //   4. Calculate distance to hit (thickness)
            //   5. Store in result.VertexThickness
            //   6. If thickness < threshold, add to result.ThinVertices

            // Placeholder implementation - just return empty result for now
            foreach (int vid in mesh.VertexIndices())
            {
                result.VertexThickness[vid] = 0.0; // TODO: Calculate actual thickness
            }

            return result;
        }
    }

    /// <summary>
    /// Result of thickness analysis containing per-vertex measurements.
    /// </summary>
    public class ThicknessAnalysisResult
    {
        /// <summary>
        /// Thickness value for each vertex (vertex ID -> thickness in mm)
        /// </summary>
        public System.Collections.Generic.Dictionary<int, double> VertexThickness { get; set; }
            = new System.Collections.Generic.Dictionary<int, double>();

        /// <summary>
        /// List of vertex IDs that are below the thickness threshold
        /// </summary>
        public System.Collections.Generic.List<int> ThinVertices { get; set; }
            = new System.Collections.Generic.List<int>();
    }

    #endregion
}
