using System.ComponentModel;
using System.Text.Json;
using k8s;
using k8s.Models;
using Microsoft.Extensions.AI;

namespace KubernetesAgent;

// Agent'ın cluster'da değişiklik yapabildiği TEK aksiyon. Kubernetes MCP salt okunur kalır; yazma işlemleri
// burada tek tek tanımlanır ve her biri insan onayı gerektirir (ApprovalRequiredAIFunction).
public sealed class KubernetesActions(IKubernetes client)
{
    // Onay verilse bile dokunulmayan namespace'ler
    private static readonly HashSet<string> ProtectedNamespaces =
        ["kube-system", "kube-public", "kube-node-lease", "local-path-storage"];

    public static KubernetesActions CreateFromKubeConfig()
    {
        // Host'taki ~/.kube/config; context varsayılan olarak Docker Desktop cluster'ı
        var context = Environment.GetEnvironmentVariable("KUBERNETES_CONTEXT") ?? "docker-desktop";
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: context);
        return new KubernetesActions(new Kubernetes(config));
    }

    public IEnumerable<AITool> AsApprovalRequiredTools()
    {
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(RestartDeploymentAsync,
            new AIFunctionFactoryOptions { Name = "kubernetes_restart_deployment" }));
    }

    [Description("Bir Deployment'ın pod'larını sırayla yeniden başlatır (kubectl rollout restart ile aynı). " +
                 "Cluster'ı DEĞİŞTİRİR; çalışmadan önce kullanıcının onayı istenir. Sistem namespace'lerinde çalışmaz.")]
    private async Task<string> RestartDeploymentAsync(
        [Description("Deployment'ın namespace'i")] string @namespace,
        [Description("Deployment adı")] string name,
        [Description("Neden yeniden başlatıldığı (bulgulara dayanan kısa gerekçe)")] string reason,
        CancellationToken cancellationToken)
    {
        if (ProtectedNamespaces.Contains(@namespace))
            return $"REDDEDİLDİ: '{@namespace}' korumalı bir sistem namespace'i; bu agent burada değişiklik yapamaz.";

        try
        {
            await client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: cancellationToken);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return $"BULUNAMADI: {@namespace}/{name} adında bir Deployment yok.";
        }

        // kubectl rollout restart'ın yaptığı gibi pod template'ine restartedAt annotation'ı eklenir
        var restartedAt = DateTime.UtcNow.ToString("o");
        var patch = new
        {
            spec = new
            {
                template = new
                {
                    metadata = new
                    {
                        annotations = new Dictionary<string, string>
                        {
                            ["kubectl.kubernetes.io/restartedAt"] = restartedAt,
                            ["sessionprojects.io/restart-reason"] = reason
                        }
                    }
                }
            }
        };

        await client.AppsV1.PatchNamespacedDeploymentAsync(
            new V1Patch(JsonSerializer.Serialize(patch), V1Patch.PatchType.StrategicMergePatch),
            name, @namespace, cancellationToken: cancellationToken);

        return $"TAMAM: {@namespace}/{name} yeniden başlatıldı (restartedAt={restartedAt}). " +
               "Yeni pod'ların Running/Ready olduğunu pods_list ile doğrula.";
    }
}
