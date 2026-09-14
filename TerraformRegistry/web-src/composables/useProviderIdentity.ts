/**
 * Visual identity for a Terraform provider.
 *
 * Colour appears on this page only here, where it carries information: which
 * cloud a module targets is the fastest way to narrow a long list by eye.
 * Everything else stays on the registry's monochrome zinc palette.
 */
export interface ProviderIdentity {
  icon: string;
  color: string;
}

const FALLBACK: ProviderIdentity = {
  icon: "i-lucide-cloud",
  color: "#a1a1aa",
};

// Keyed by the provider string Terraform uses in a module source coordinate.
const IDENTITIES: Record<string, ProviderIdentity> = {
  aws: { icon: "i-simple-icons-amazonwebservices", color: "#FF9900" },
  azurerm: { icon: "i-simple-icons-microsoftazure", color: "#0078D4" },
  azuread: { icon: "i-simple-icons-microsoftazure", color: "#0078D4" },
  azure: { icon: "i-simple-icons-microsoftazure", color: "#0078D4" },
  google: { icon: "i-simple-icons-googlecloud", color: "#4285F4" },
  "google-beta": { icon: "i-simple-icons-googlecloud", color: "#4285F4" },
  gcp: { icon: "i-simple-icons-googlecloud", color: "#4285F4" },
  kubernetes: { icon: "i-simple-icons-kubernetes", color: "#326CE5" },
  k8s: { icon: "i-simple-icons-kubernetes", color: "#326CE5" },
  helm: { icon: "i-simple-icons-helm", color: "#5B7FDE" },
  docker: { icon: "i-simple-icons-docker", color: "#2496ED" },
  github: { icon: "i-simple-icons-github", color: "#e4e4e7" },
  gitlab: { icon: "i-simple-icons-gitlab", color: "#FC6D26" },
  cloudflare: { icon: "i-simple-icons-cloudflare", color: "#F38020" },
  digitalocean: { icon: "i-simple-icons-digitalocean", color: "#0080FF" },
  postgresql: { icon: "i-simple-icons-postgresql", color: "#4169E1" },
  mysql: { icon: "i-simple-icons-mysql", color: "#4479A1" },
  redis: { icon: "i-simple-icons-redis", color: "#FF4438" },
  vault: { icon: "i-simple-icons-vault", color: "#FFEC6E" },
  consul: { icon: "i-simple-icons-consul", color: "#F24C53" },
  nomad: { icon: "i-simple-icons-nomad", color: "#00CA8E" },
  oracle: { icon: "i-simple-icons-oracle", color: "#F80000" },
  oci: { icon: "i-simple-icons-oracle", color: "#F80000" },
  alicloud: { icon: "i-simple-icons-alibabacloud", color: "#FF6A00" },
  openstack: { icon: "i-simple-icons-openstack", color: "#ED1944" },
  vsphere: { icon: "i-simple-icons-vmware", color: "#8A9BA8" },
  vmware: { icon: "i-simple-icons-vmware", color: "#8A9BA8" },
  datadog: { icon: "i-simple-icons-datadog", color: "#632CA6" },
  elasticsearch: { icon: "i-simple-icons-elastic", color: "#43A047" },
  null: { icon: "i-simple-icons-terraform", color: "#7B42BC" },
  local: { icon: "i-simple-icons-terraform", color: "#7B42BC" },
  random: { icon: "i-simple-icons-terraform", color: "#7B42BC" },
};

export function useProviderIdentity() {
  const providerIdentity = (provider: string): ProviderIdentity =>
    IDENTITIES[provider?.toLowerCase()] ?? FALLBACK;

  return { providerIdentity };
}
