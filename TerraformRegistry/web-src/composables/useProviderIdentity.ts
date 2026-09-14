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

// Icon names use Iconify's canonical "collection:icon" form rather than the
// "i-collection-icon" shorthand: the shorthand splits at the first hyphen, so
// a collection whose own name contains one — simple-icons — cannot be resolved
// from the client bundle.
const FALLBACK: ProviderIdentity = {
  icon: "lucide:cloud",
  color: "#a1a1aa",
};

// Keyed by the provider string Terraform uses in a module source coordinate.
const IDENTITIES: Record<string, ProviderIdentity> = {
  aws: { icon: "simple-icons:amazonwebservices", color: "#FF9900" },
  azurerm: { icon: "simple-icons:microsoftazure", color: "#0078D4" },
  azuread: { icon: "simple-icons:microsoftazure", color: "#0078D4" },
  azure: { icon: "simple-icons:microsoftazure", color: "#0078D4" },
  google: { icon: "simple-icons:googlecloud", color: "#4285F4" },
  "google-beta": { icon: "simple-icons:googlecloud", color: "#4285F4" },
  gcp: { icon: "simple-icons:googlecloud", color: "#4285F4" },
  kubernetes: { icon: "simple-icons:kubernetes", color: "#326CE5" },
  k8s: { icon: "simple-icons:kubernetes", color: "#326CE5" },
  helm: { icon: "simple-icons:helm", color: "#5B7FDE" },
  docker: { icon: "simple-icons:docker", color: "#2496ED" },
  github: { icon: "simple-icons:github", color: "#e4e4e7" },
  gitlab: { icon: "simple-icons:gitlab", color: "#FC6D26" },
  cloudflare: { icon: "simple-icons:cloudflare", color: "#F38020" },
  digitalocean: { icon: "simple-icons:digitalocean", color: "#0080FF" },
  postgresql: { icon: "simple-icons:postgresql", color: "#4169E1" },
  mysql: { icon: "simple-icons:mysql", color: "#4479A1" },
  redis: { icon: "simple-icons:redis", color: "#FF4438" },
  vault: { icon: "simple-icons:vault", color: "#FFEC6E" },
  consul: { icon: "simple-icons:consul", color: "#F24C53" },
  nomad: { icon: "simple-icons:nomad", color: "#00CA8E" },
  oracle: { icon: "simple-icons:oracle", color: "#F80000" },
  oci: { icon: "simple-icons:oracle", color: "#F80000" },
  alicloud: { icon: "simple-icons:alibabacloud", color: "#FF6A00" },
  openstack: { icon: "simple-icons:openstack", color: "#ED1944" },
  vsphere: { icon: "simple-icons:vmware", color: "#8A9BA8" },
  vmware: { icon: "simple-icons:vmware", color: "#8A9BA8" },
  datadog: { icon: "simple-icons:datadog", color: "#632CA6" },
  elasticsearch: { icon: "simple-icons:elastic", color: "#43A047" },
  null: { icon: "simple-icons:terraform", color: "#7B42BC" },
  local: { icon: "simple-icons:terraform", color: "#7B42BC" },
  random: { icon: "simple-icons:terraform", color: "#7B42BC" },
};

export function useProviderIdentity() {
  const providerIdentity = (provider: string): ProviderIdentity =>
    IDENTITIES[provider?.toLowerCase()] ?? FALLBACK;

  return { providerIdentity };
}
