using System.Numerics;
using Aurelian.Rendering.Contracts.Models;

/// <summary>Double-precision paper equations for qualification, independent of shader compilation.</summary>
internal static class DiffuseReference
{
    public static double Factor(DirectDiffuseModel3D model, double nl, double nv, double lv, double roughness)
    {
        if (model == DirectDiffuseModel3D.Lambert)
        {
            return 1;
        }
        if (model == DirectDiffuseModel3D.PathDiffuse)
        {
            return Response(model, 1, nl, nv, lv, roughness);
        }
        if (model == DirectDiffuseModel3D.Eon)
        {
            if (roughness == 0)
            {
                return 1;
            }
            double normalization = 1 / (1 + (.5 - 2 / (3 * Math.PI)) * roughness);
            double tangentDot = lv - nl * nv;
            double projected = tangentDot > 0 ? tangentDot / Math.Max(nl, nv) : tangentDot;
            double single = normalization * (1 + roughness * projected);
            double average = normalization * (1 + (2.0 / 3 - 28 / (15 * Math.PI)) * roughness);
            // Exact directional integral, rather than the production shader's polynomial fit.
            return single + (1 - FonAlbedo(nl, roughness)) * (1 - FonAlbedo(nv, roughness)) / (1 - average);
        }
        double halfCosineSquared = Math.Max(0, (1 + lv) * .5);
        double grazing = .5 + 2 * roughness * halfCosineSquared;
        double energy = 1;
        if (model == DirectDiffuseModel3D.Frostbite)
        {
            grazing = .5 * roughness + 2 * roughness * halfCosineSquared;
            energy = 1 + (1 / 1.51 - 1) * roughness;
        }
        return (1 + (grazing - 1) * Math.Pow(1 - nl, 5))
            * (1 + (grazing - 1) * Math.Pow(1 - nv, 5)) * energy;
    }

    public static double Response(DirectDiffuseModel3D model, double albedo, double nl, double nv, double lv, double roughness)
    {
        if (model != DirectDiffuseModel3D.PathDiffuse)
        {
            return albedo * Factor(model, nl, nv, lv, roughness);
        }
        // Independent scalar reference: sum the bounce series instead of the shader's reciprocal.
        double attenuation = 1 - (1 - 1 / 1.51) * roughness;
        double edge = roughness / 2;
        double incident = 1 - edge * Math.Pow(1 - nl, 5);
        double outgoing = 1 - edge * Math.Pow(1 - nv, 5);
        double integral = 1 - edge / 21;
        double average = attenuation * integral * integral;
        double missing = 1 - average;
        if (missing < 1e-14)
        {
            return albedo;
        }
        double bounce = albedo * albedo * average;
        double multipleAlbedo = 0;
        for (int order = 0; order < 64; order++)
        {
            multipleAlbedo += bounce;
            bounce *= albedo * missing;
            if (bounce < 1e-15)
            {
                break;
            }
        }
        double residualIncident = 1 - attenuation * integral * incident;
        double residualOutgoing = 1 - attenuation * integral * outgoing;
        return albedo * attenuation * incident * outgoing
            + multipleAlbedo * residualIncident * residualOutgoing / missing;
    }

    public static object QualifyPath()
    {
        double maximumIntegrationError = 0;
        double maximumReciprocityError = 0;
        double maximumReflectance = 0;
        int cases = 0;
        foreach (double roughness in new[] { 0, .045, .25, .65, 1 })
        {
            foreach (double nv in new[] { 0, .05, .25, .7, 1 })
            {
                foreach (double albedo in new[] { 0, .025, .075, .18, .42, .65, 1 })
                {
                    double integral = 0;
                    const int Cosines = 8192;
                    for (int sample = 0; sample < Cosines; sample++)
                    {
                        double nl = Math.Sqrt((sample + .5) / Cosines);
                        double value = Response(DirectDiffuseModel3D.PathDiffuse, albedo, nl, nv, 0, roughness);
                        double reverse = Response(DirectDiffuseModel3D.PathDiffuse, albedo, nv, nl, 0, roughness);
                        if (!double.IsFinite(value) || value < 0)
                        {
                            throw new InvalidDataException("Path diffuse produced a negative or nonfinite BRDF.");
                        }
                        maximumReciprocityError = Math.Max(maximumReciprocityError, Math.Abs(value - reverse));
                        integral += value / Cosines;
                    }
                    double attenuation = 1 + (1 / 1.51 - 1) * roughness;
                    double edge = roughness / 2;
                    double average = attenuation * Math.Pow(1 - edge / 21, 2);
                    double directional = attenuation * (1 - edge / 21) * (1 - edge * Math.Pow(1 - nv, 5));
                    double bounce = albedo * albedo * average / (1 - albedo * (1 - average));
                    double expected = albedo * directional + bounce * (1 - directional);
                    maximumIntegrationError = Math.Max(maximumIntegrationError, Math.Abs(integral - expected));
                    maximumReflectance = Math.Max(maximumReflectance, integral);
                    if (integral > albedo + .0001 || Math.Abs(integral - expected) > .0001)
                    {
                        throw new InvalidDataException($"Path diffuse furnace mismatch: rho={albedo}, roughness={roughness}, nv={nv}, integral={integral}, expected={expected}.");
                    }
                    cases++;
                }
            }
        }
        if (maximumReciprocityError > 1e-12)
        {
            throw new InvalidDataException("Path diffuse violated reciprocity.");
        }
        return new
        {
            Cases = cases,
            MaximumIntegrationError = maximumIntegrationError,
            MaximumReciprocityError = maximumReciprocityError,
            MaximumReflectance = maximumReflectance,
            Scope = "Isolated diffuse BRDF, without the separate GGX/Fresnel partition. Black/white/colored furnaces, roughness-zero Lambert limit, and reciprocal directions including the horizon."
        };
    }

    private static double FonAlbedo(double mu, double roughness)
    {
        double sine = Math.Sqrt(Math.Max(0, 1 - mu * mu));
        double integral = sine * (Math.Acos(mu) - sine * mu)
            + 2.0 / 3 * (sine * mu * (1 + sine + sine * sine) / (1 + sine) - sine);
        return (1 + roughness * integral / Math.PI) / (1 + (.5 - 2 / (3 * Math.PI)) * roughness);
    }

    public static Vector3 Direct(DirectDiffuseModel3D model, Vector3 color, Vector3 light, Vector3 view, float roughness, float metallic)
    {
        double nl = light.Z;
        double nv = view.Z;
        double lv = Vector3.Dot(light, view);
        double halfLength = Math.Sqrt(2 + 2 * lv);
        double nh = (nl + nv) / halfLength;
        double vh = halfLength / 2;
        double alphaSquared = Math.Pow(roughness, 4);
        double denominator = nh * nh * (alphaSquared - 1) + 1;
        double distribution = alphaSquared / Math.Max(Math.PI * denominator * denominator, .000001);
        double k = Math.Pow(roughness + 1, 2) / 8;
        double geometry = nv / (nv * (1 - k) + k) * nl / (nl * (1 - k) + k);
        double specular = distribution * geometry / Math.Max(4 * nv * nl, .0001);
        float Channel(float albedo)
        {
            double f0 = .04 * (1 - metallic) + albedo * metallic;
            double fresnel = f0 + (1 - f0) * Math.Pow(1 - vh, 5);
            double diffuse = Response(model, albedo, nl, nv, lv, roughness);
            return (float)(((1 - fresnel) * (1 - metallic) / Math.PI * diffuse + fresnel * specular) * nl);
        }
        return new(Channel(color.X), Channel(color.Y), Channel(color.Z));
    }

    public static IReadOnlyList<FurnaceSample> Furnace()
    {
        const int Cosines = 128;
        const int Azimuths = 256;
        var result = new List<FurnaceSample>();
        foreach (DirectDiffuseModel3D model in Enum.GetValues<DirectDiffuseModel3D>())
        {
            foreach (double roughness in new[] { 0, .25, .65, 1 })
            {
                foreach (double nv in new[] { 1, .7, .25, .05 })
                {
                    double total = 0;
                    for (int polar = 0; polar < Cosines; polar++)
                    {
                        double nl = Math.Sqrt((polar + .5) / Cosines);
                        double tangent = Math.Sqrt(1 - nl * nl) * Math.Sqrt(1 - nv * nv);
                        for (int azimuth = 0; azimuth < Azimuths; azimuth++)
                        {
                            double phi = Math.Tau * (azimuth + .5) / Azimuths;
                            double value = Factor(model, nl, nv, nl * nv + tangent * Math.Cos(phi), roughness);
                            if (!double.IsFinite(value) || value < 0)
                            {
                                throw new InvalidDataException("Diffuse BRDF produced a nonfinite or negative response.");
                            }
                            total += value;
                        }
                    }
                    result.Add(new(model.ToString(), roughness, nv, total / (Cosines * Azimuths)));
                }
            }
        }
        return result;
    }
}

internal sealed record FurnaceSample(string Model, double Roughness, double ViewCosine, double Reflectance);
