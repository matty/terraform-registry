<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import { useWebhooks, WEBHOOK_EVENTS, WEBHOOK_FORMATS, TEMPLATE_VARIABLES } from "~/composables/useWebhooks";
import type { Webhook } from "~/composables/useWebhooks";

definePageMeta({
  middleware: "auth",
});

const { isSidebarOpen } = useDashboard();
const { listWebhooks, createWebhook, updateWebhook, deleteWebhook, testWebhook } = useWebhooks();

// State
const webhooks = ref<Webhook[]>([]);
const isLoading = ref(false);
const isCreating = ref(false);
const errorMessage = ref<string | null>(null);

// Create form
const newUrl = ref("");
const newSecret = ref("");
const newEvents = ref<string[]>([]);
const newFormat = ref("generic");
const newTemplate = ref("");
const newCustomTitle = ref("");
const newCustomBody = ref("");

// Edit state
const editingWebhook = ref<Webhook | null>(null);
const editUrl = ref("");
const editSecret = ref("");
const editEvents = ref<string[]>([]);
const editFormat = ref("generic");
const editTemplate = ref("");
const editCustomTitle = ref("");
const editCustomBody = ref("");
const isEditModalOpen = ref(false);

// Delete confirmation
const isDeleteModalOpen = ref(false);
const webhookToDelete = ref<string | null>(null);

// Test state
const testingWebhookId = ref<string | null>(null);
const testResult = ref<{ success: boolean, message: string } | null>(null);

const handleTest = async (id: string) => {
  testingWebhookId.value = id;
  testResult.value = null;
  try {
    const result = await testWebhook(id);
    testResult.value = { success: true, message: result.message || "Test delivered" };
  } catch (e: any) {
    const msg = e?.data?.error || e?.message || "Test delivery failed";
    testResult.value = { success: false, message: msg };
  } finally {
    testingWebhookId.value = null;
    // Auto-clear result after 4 seconds
    setTimeout(() => { testResult.value = null; }, 4000);
  }
};

const fetchWebhooks = async () => {
  isLoading.value = true;
  errorMessage.value = null;
  try {
    webhooks.value = await listWebhooks();
  } catch (e) {
    console.error("Failed to fetch webhooks", e);
    errorMessage.value = "Failed to load webhooks.";
  } finally {
    isLoading.value = false;
  }
};

const computeTemplate = (format: string, customTitle: string, customBody: string, rawTemplate: string): string | undefined => {
  if (format === "discord" || format === "slack" || format === "teams") {
    if (customTitle || customBody) {
      return JSON.stringify({ title: customTitle, body: customBody });
    }
    return undefined;
  }
  if (format === "custom") {
    return rawTemplate || undefined;
  }
  return undefined;
};

const handleCreate = async () => {
  if (!newUrl.value || newEvents.value.length === 0) return;
  isCreating.value = true;
  errorMessage.value = null;
  try {
    const template = computeTemplate(newFormat.value, newCustomTitle.value, newCustomBody.value, newTemplate.value);
    await createWebhook({ url: newUrl.value, events: newEvents.value, secret: newSecret.value || undefined, format: newFormat.value, template });
    newUrl.value = "";
    newSecret.value = "";
    newEvents.value = [];
    newFormat.value = "generic";
    newTemplate.value = "";
    newCustomTitle.value = "";
    newCustomBody.value = "";
    await fetchWebhooks();
  } catch (e) {
    console.error("Failed to create webhook", e);
    errorMessage.value = "Failed to create webhook.";
  } finally {
    isCreating.value = false;
  }
};

const openEdit = (webhook: Webhook) => {
  editingWebhook.value = webhook;
  editUrl.value = webhook.url;
  editSecret.value = "";
  editEvents.value = [...webhook.events];
  editFormat.value = webhook.format || "generic";
  editTemplate.value = "";
  editCustomTitle.value = "";
  editCustomBody.value = "";
  if (webhook.template) {
    if (editFormat.value === "discord" || editFormat.value === "slack" || editFormat.value === "teams") {
      try {
        const parsed = JSON.parse(webhook.template);
        editCustomTitle.value = parsed.title || "";
        editCustomBody.value = parsed.body || "";
      } catch {
        editCustomTitle.value = "";
        editCustomBody.value = "";
      }
    } else if (editFormat.value === "custom") {
      editTemplate.value = webhook.template;
    }
  }
  isEditModalOpen.value = true;
};

const handleUpdate = async () => {
  if (!editingWebhook.value) return;
  errorMessage.value = null;
  try {
    const template = computeTemplate(editFormat.value, editCustomTitle.value, editCustomBody.value, editTemplate.value);
    await updateWebhook(editingWebhook.value.id, {
      url: editUrl.value,
      events: editEvents.value,
      secret: editSecret.value || undefined,
      format: editFormat.value,
      template,
    });
    isEditModalOpen.value = false;
    editingWebhook.value = null;
    await fetchWebhooks();
  } catch (e) {
    console.error("Failed to update webhook", e);
    errorMessage.value = "Failed to update webhook.";
  }
};

const toggleActive = async (webhook: Webhook) => {
  errorMessage.value = null;
  try {
    await updateWebhook(webhook.id, { isActive: !webhook.isActive });
    await fetchWebhooks();
  } catch (e) {
    console.error("Failed to toggle webhook", e);
    errorMessage.value = "Failed to update webhook status.";
  }
};

const confirmDelete = (id: string) => {
  webhookToDelete.value = id;
  isDeleteModalOpen.value = true;
};

const handleDelete = async () => {
  if (!webhookToDelete.value) return;
  errorMessage.value = null;
  try {
    await deleteWebhook(webhookToDelete.value);
    await fetchWebhooks();
  } catch (e) {
    console.error("Failed to delete webhook", e);
    errorMessage.value = "Failed to delete webhook.";
  } finally {
    isDeleteModalOpen.value = false;
    webhookToDelete.value = null;
  }
};

const formatColor = (format: string): string => {
  const colors: Record<string, string> = {
    generic: "bg-neutral-800 text-neutral-400",
    discord: "bg-indigo-900/40 text-indigo-300",
    slack: "bg-green-900/40 text-green-300",
    teams: "bg-blue-900/40 text-blue-300",
    custom: "bg-amber-900/40 text-amber-300",
  };
  return colors[format] ?? "bg-neutral-800 text-neutral-400";
};

const formatLabel = (format: string): string => {
  const entry = WEBHOOK_FORMATS.find(f => f.value === format);
  return entry ? entry.label : format;
};

const formatOptions = WEBHOOK_FORMATS.map(f => ({ label: f.label, value: f.value }));
const templateVariablesText = TEMPLATE_VARIABLES.join(', ');

const eventColor = (event: string): string => {
  const colors: Record<string, string> = {
    "module.published": "bg-green-900/40 text-green-300",
    "module.deleted": "bg-red-900/40 text-red-300",
    "module.restored": "bg-blue-900/40 text-blue-300",
    "module.purged": "bg-orange-900/40 text-orange-300",
  };
  return colors[event] ?? "bg-neutral-800 text-neutral-400";
};

const eventDotColor = (event: string): string => {
  const colors: Record<string, string> = {
    "module.published": "bg-green-400",
    "module.deleted": "bg-red-400",
    "module.restored": "bg-blue-400",
    "module.purged": "bg-orange-400",
  };
  return colors[event] ?? "bg-neutral-400";
};

const eventBorderColor = (event: string): string => {
  const colors: Record<string, string> = {
    "module.published": "border-green-500/50 hover:border-green-400",
    "module.deleted": "border-red-500/50 hover:border-red-400",
    "module.restored": "border-blue-500/50 hover:border-blue-400",
    "module.purged": "border-orange-500/50 hover:border-orange-400",
  };
  return colors[event] ?? "border-neutral-600 hover:border-neutral-500";
};

const eventSelectedBg = (event: string): string => {
  const colors: Record<string, string> = {
    "module.published": "bg-green-500/20 border-green-400/60 shadow-green-500/10",
    "module.deleted": "bg-red-500/20 border-red-400/60 shadow-red-500/10",
    "module.restored": "bg-blue-500/20 border-blue-400/60 shadow-blue-500/10",
    "module.purged": "bg-orange-500/20 border-orange-400/60 shadow-orange-500/10",
  };
  return colors[event] ?? "bg-neutral-700 border-neutral-500";
};

// Format card metadata
const formatCards = [
  { value: 'generic', label: 'Generic', description: 'Standard JSON payload', icon: 'i-lucide-code', color: 'neutral', borderColor: 'border-neutral-500/50', glowColor: 'shadow-neutral-500/20', selectedBg: 'bg-neutral-500/10' },
  { value: 'discord', label: 'Discord', description: 'Rich embed message', icon: 'i-lucide-message-circle', color: 'indigo', borderColor: 'border-indigo-500/50', glowColor: 'shadow-indigo-500/20', selectedBg: 'bg-indigo-500/10' },
  { value: 'slack', label: 'Slack', description: 'Block Kit message', icon: 'i-lucide-hash', color: 'green', borderColor: 'border-green-500/50', glowColor: 'shadow-green-500/20', selectedBg: 'bg-green-500/10' },
  { value: 'teams', label: 'Teams', description: 'Adaptive card', icon: 'i-lucide-monitor', color: 'blue', borderColor: 'border-blue-500/50', glowColor: 'shadow-blue-500/20', selectedBg: 'bg-blue-500/10' },
  { value: 'custom', label: 'Custom', description: 'Your own template', icon: 'i-lucide-file-code', color: 'amber', borderColor: 'border-amber-500/50', glowColor: 'shadow-amber-500/20', selectedBg: 'bg-amber-500/10' },
];

const formatCardBorderLeft = (format: string): string => {
  const colors: Record<string, string> = {
    generic: "border-l-neutral-500/60",
    discord: "border-l-indigo-500/60",
    slack: "border-l-green-500/60",
    teams: "border-l-blue-500/60",
    custom: "border-l-amber-500/60",
  };
  return colors[format] ?? "border-l-neutral-500/60";
};

const formatIconClass = (format: string): string => {
  const colors: Record<string, string> = {
    generic: "text-neutral-400",
    discord: "text-indigo-400",
    slack: "text-green-400",
    teams: "text-blue-400",
    custom: "text-amber-400",
  };
  return colors[format] ?? "text-neutral-400";
};

const formatIconName = (format: string): string => {
  const icons: Record<string, string> = {
    generic: "i-lucide-code",
    discord: "i-lucide-message-circle",
    slack: "i-lucide-hash",
    teams: "i-lucide-monitor",
    custom: "i-lucide-file-code",
  };
  return icons[format] ?? "i-lucide-code";
};

const toggleNewEvent = (event: string) => {
  const idx = newEvents.value.indexOf(event);
  if (idx === -1) {
    newEvents.value.push(event);
  } else {
    newEvents.value.splice(idx, 1);
  }
};

const toggleEditEvent = (event: string) => {
  const idx = editEvents.value.indexOf(event);
  if (idx === -1) {
    editEvents.value.push(event);
  } else {
    editEvents.value.splice(idx, 1);
  }
};

// Preview helpers
const previewTitle = computed(() => {
  const t = newCustomTitle.value || '{{module.name}} v{{module.version}} published';
  return t.replace(/\{\{([^}]+)\}\}/g, '<span class="text-primary-400 font-medium">$1</span>');
});

const previewBody = computed(() => {
  const b = newCustomBody.value || '{{module.description}}';
  return b.replace(/\{\{([^}]+)\}\}/g, '<span class="text-primary-400 font-medium">$1</span>');
});

onMounted(() => {
  fetchWebhooks();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Mobile menu button -->
    <div class="lg:hidden px-4 pt-4">
      <UButton
        icon="i-lucide-menu"
        variant="ghost"
        color="neutral"
        @click="isSidebarOpen = true"
      />
    </div>

    <!-- Page Header -->
    <div class="page-header">
      <div class="flex items-center justify-between">
        <div>
          <h1 class="page-header-title">Webhooks</h1>
          <p class="page-header-subtitle">Receive HTTP notifications when registry events occur</p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-4xl space-y-8">

        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3 backdrop-blur-sm"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl shrink-0" />
          <p class="text-sm text-red-300">{{ errorMessage }}</p>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            class="ml-auto"
            @click="errorMessage = null"
          />
        </div>

        <!-- Test Result Notification -->
        <Transition name="slide-fade">
          <div
            v-if="testResult"
            :class="[
              'p-4 rounded-xl flex items-center gap-3 backdrop-blur-sm',
              testResult.success
                ? 'bg-green-900/20 border border-green-800/50'
                : 'bg-red-900/20 border border-red-800/50'
            ]"
          >
            <UIcon
              :name="testResult.success ? 'i-lucide-check-circle' : 'i-lucide-alert-circle'"
              :class="testResult.success ? 'text-green-500 text-xl' : 'text-red-500 text-xl'"
            />
            <p :class="testResult.success ? 'text-sm text-green-300' : 'text-sm text-red-300'">
              {{ testResult.message }}
            </p>
            <UButton
              icon="i-lucide-x"
              color="neutral"
              variant="ghost"
              size="sm"
              class="ml-auto"
              @click="testResult = null"
            />
          </div>
        </Transition>

        <!-- Create Webhook Form -->
        <div class="webhook-create-card rounded-2xl border border-neutral-800/80 overflow-hidden">
          <!-- Card header -->
          <div class="px-6 py-5 border-b border-neutral-800/60 bg-neutral-900/40">
            <h3 class="text-base font-semibold text-neutral-100 flex items-center gap-3">
              <div class="w-9 h-9 rounded-xl bg-primary-500/15 flex items-center justify-center">
                <UIcon name="i-lucide-plus" class="text-primary-400 text-lg" />
              </div>
              Add Webhook
            </h3>
          </div>

          <div class="p-6 space-y-7">
            <!-- Step 1: URL -->
            <div class="space-y-2">
              <div class="flex items-center gap-2 mb-1">
                <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold">1</span>
                <label class="text-sm font-medium text-neutral-300">Endpoint URL</label>
              </div>
              <UInput
                v-model="newUrl"
                placeholder="https://example.com/webhook"
                size="lg"
                class="webhook-input"
                @keyup.enter="handleCreate"
              />
            </div>

            <!-- Step 2: Secret -->
            <div class="space-y-2">
              <div class="flex items-center gap-2 mb-1">
                <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold">2</span>
                <label class="text-sm font-medium text-neutral-300">Signing Secret</label>
                <span class="text-xs text-neutral-500 ml-1">optional</span>
              </div>
              <UInput
                v-model="newSecret"
                type="password"
                placeholder="HMAC-SHA256 signing secret"
                class="webhook-input"
              />
            </div>

            <!-- Step 3: Format -->
            <div class="space-y-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold">3</span>
                <label class="text-sm font-medium text-neutral-300">Payload Format</label>
              </div>
              <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
                <button
                  v-for="card in formatCards"
                  :key="card.value"
                  type="button"
                  :class="[
                    'format-card group relative flex flex-col items-center gap-2 p-4 rounded-xl border transition-all duration-200 cursor-pointer text-center',
                    newFormat === card.value
                      ? `${card.selectedBg} ${card.borderColor} shadow-lg ${card.glowColor} ring-1 ring-${card.color}-500/30`
                      : 'bg-neutral-900/40 border-neutral-700/50 hover:border-neutral-600 hover:bg-neutral-800/40'
                  ]"
                  @click="newFormat = card.value"
                >
                  <UIcon
                    :name="card.icon"
                    :class="[
                      'text-2xl transition-colors',
                      newFormat === card.value ? `text-${card.color}-400` : 'text-neutral-500 group-hover:text-neutral-400'
                    ]"
                  />
                  <span :class="[
                    'text-sm font-medium transition-colors',
                    newFormat === card.value ? 'text-neutral-100' : 'text-neutral-400 group-hover:text-neutral-300'
                  ]">
                    {{ card.label }}
                  </span>
                  <span class="text-[11px] text-neutral-500 leading-tight">{{ card.description }}</span>
                  <!-- Selected indicator -->
                  <div
                    v-if="newFormat === card.value"
                    class="absolute -top-1 -right-1 w-5 h-5 rounded-full bg-primary-500 flex items-center justify-center"
                  >
                    <UIcon name="i-lucide-check" class="text-white text-xs" />
                  </div>
                </button>
              </div>
            </div>

            <!-- Live Preview for Discord/Slack/Teams -->
            <Transition name="slide-fade">
              <div v-if="newFormat === 'discord' || newFormat === 'slack' || newFormat === 'teams'" class="space-y-4">
                <div class="space-y-3">
                  <p class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Custom Overrides</p>
                  <UInput
                    v-model="newCustomTitle"
                    placeholder="{{module.name}} v{{module.version}} published"
                    class="webhook-input"
                  />
                  <UInput
                    v-model="newCustomBody"
                    placeholder="{{module.description}}"
                    class="webhook-input"
                  />
                </div>

                <!-- Preview Panel -->
                <div class="rounded-xl border border-neutral-700/50 overflow-hidden">
                  <div class="px-4 py-2.5 bg-neutral-800/60 border-b border-neutral-700/40 flex items-center gap-2">
                    <UIcon name="i-lucide-eye" class="text-neutral-500 text-sm" />
                    <span class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Preview</span>
                  </div>
                  <!-- Discord Preview -->
                  <div v-if="newFormat === 'discord'" class="p-4 bg-[#313338]">
                    <div class="flex gap-3">
                      <div class="w-10 h-10 rounded-full bg-indigo-600 flex items-center justify-center shrink-0">
                        <UIcon name="i-lucide-webhook" class="text-white text-sm" />
                      </div>
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="text-sm font-semibold text-white">Terraform Registry</span>
                          <span class="px-1.5 py-0.5 rounded bg-indigo-500/30 text-[10px] text-indigo-300 font-medium">BOT</span>
                        </div>
                        <div class="mt-2 pl-3 border-l-4 border-indigo-500 bg-[#2b2d31] rounded-r-lg p-3">
                          <p class="text-sm font-semibold text-white" v-html="previewTitle" />
                          <p class="text-sm text-neutral-300 mt-1" v-html="previewBody" />
                        </div>
                      </div>
                    </div>
                  </div>
                  <!-- Slack Preview -->
                  <div v-else-if="newFormat === 'slack'" class="p-4 bg-[#1a1d21]">
                    <div class="flex gap-3">
                      <div class="w-9 h-9 rounded-lg bg-green-600 flex items-center justify-center shrink-0">
                        <UIcon name="i-lucide-webhook" class="text-white text-sm" />
                      </div>
                      <div class="min-w-0">
                        <div class="flex items-center gap-2">
                          <span class="text-sm font-bold text-white">Terraform Registry</span>
                        </div>
                        <div class="mt-1.5 pl-3 border-l-4 border-green-500 py-1">
                          <p class="text-sm font-bold text-[#d1d2d3]" v-html="previewTitle" />
                          <p class="text-sm text-[#ababad] mt-0.5" v-html="previewBody" />
                        </div>
                      </div>
                    </div>
                  </div>
                  <!-- Teams Preview -->
                  <div v-else-if="newFormat === 'teams'" class="p-4 bg-[#292929]">
                    <div class="bg-[#333333] rounded-lg border border-[#444] overflow-hidden">
                      <div class="h-1 bg-blue-500" />
                      <div class="p-4">
                        <p class="text-sm font-semibold text-white" v-html="previewTitle" />
                        <p class="text-sm text-neutral-300 mt-1.5" v-html="previewBody" />
                        <div class="mt-3 pt-3 border-t border-[#444] flex items-center gap-2">
                          <div class="w-5 h-5 rounded bg-blue-500/30 flex items-center justify-center">
                            <UIcon name="i-lucide-webhook" class="text-blue-400 text-xs" />
                          </div>
                          <span class="text-[11px] text-neutral-500">Terraform Registry</span>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </Transition>

            <!-- Custom Template -->
            <Transition name="slide-fade">
              <div v-if="newFormat === 'custom'" class="space-y-3">
                <p class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Template Body</p>
                <UTextarea
                  v-model="newTemplate"
                  placeholder='{"text":"{{event}} - {{module.name}}"}'
                  :rows="8"
                  class="w-full font-mono text-sm webhook-input"
                />
                <div class="flex flex-wrap gap-1.5">
                  <span class="text-xs text-neutral-500 mr-1">Variables:</span>
                  <code
                    v-for="variable in TEMPLATE_VARIABLES"
                    :key="variable"
                    class="px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300/80 text-[11px] font-mono border border-amber-500/20"
                  >
                    {{ variable }}
                  </code>
                </div>
              </div>
            </Transition>

            <!-- Step 4: Events -->
            <div class="space-y-3">
              <div class="flex items-center gap-2 mb-1">
                <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold">4</span>
                <label class="text-sm font-medium text-neutral-300">Events</label>
              </div>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="event in WEBHOOK_EVENTS"
                  :key="event"
                  type="button"
                  :class="[
                    'event-pill inline-flex items-center gap-2 px-3.5 py-2 rounded-xl border text-sm font-medium transition-all duration-200 cursor-pointer',
                    newEvents.includes(event)
                      ? `${eventSelectedBg(event)} shadow-md`
                      : `bg-neutral-900/40 ${eventBorderColor(event)} text-neutral-400 hover:text-neutral-300`
                  ]"
                  @click="toggleNewEvent(event)"
                >
                  <span :class="['w-2 h-2 rounded-full shrink-0', eventDotColor(event)]" />
                  <span :class="newEvents.includes(event) ? 'text-neutral-100' : ''">{{ event }}</span>
                </button>
              </div>
            </div>

            <!-- Create button -->
            <div class="flex justify-end pt-2 border-t border-neutral-800/50">
              <UButton
                icon="i-lucide-webhook"
                label="Create Webhook"
                color="primary"
                size="lg"
                :loading="isCreating"
                :disabled="!newUrl || newEvents.length === 0"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Webhooks List -->
        <div class="space-y-4">
          <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-neutral-800 flex items-center justify-center">
              <UIcon name="i-lucide-webhook" class="text-primary-400" />
            </div>
            Your Webhooks
            <span v-if="webhooks.length > 0" class="ml-1 px-2 py-0.5 rounded-full bg-neutral-800 text-neutral-400 text-xs font-medium">
              {{ webhooks.length }}
            </span>
          </h2>

          <div v-if="isLoading" class="py-12 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-3xl text-primary-400"
            />
          </div>

          <div
            v-else-if="webhooks.length === 0"
            class="py-12 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
          >
            <UIcon name="i-lucide-webhook" class="text-4xl text-neutral-700 mb-3" />
            <p class="text-neutral-500">No webhooks configured yet</p>
            <p class="text-sm text-neutral-600 mt-1">Create one above to start receiving event notifications</p>
          </div>

          <div v-else class="space-y-3">
            <div
              v-for="webhook in webhooks"
              :key="webhook.id"
              :class="[
                'webhook-list-card rounded-xl border border-l-4 border-neutral-800 transition-all duration-200 hover:border-neutral-700 overflow-hidden',
                formatCardBorderLeft(webhook.format)
              ]"
            >
              <div class="p-5">
                <div class="flex items-start justify-between gap-4">
                  <div class="min-w-0 flex-1 space-y-3">
                    <!-- Header row -->
                    <div class="flex items-center gap-3">
                      <div :class="[
                        'w-8 h-8 rounded-lg flex items-center justify-center shrink-0',
                        formatColor(webhook.format)
                      ]">
                        <UIcon :name="formatIconName(webhook.format)" :class="formatIconClass(webhook.format)" />
                      </div>
                      <span :class="[
                        'px-2.5 py-0.5 rounded-full text-[11px] font-semibold uppercase tracking-wide',
                        formatColor(webhook.format)
                      ]">
                        {{ formatLabel(webhook.format) }}
                      </span>
                      <span
                        :class="[
                          'flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium',
                          webhook.isActive
                            ? 'bg-green-900/40 text-green-300'
                            : 'bg-neutral-800 text-neutral-400',
                        ]"
                      >
                        <span :class="['w-1.5 h-1.5 rounded-full', webhook.isActive ? 'bg-green-400 animate-pulse' : 'bg-neutral-500']" />
                        {{ webhook.isActive ? "Active" : "Inactive" }}
                      </span>
                    </div>

                    <!-- URL -->
                    <div class="font-mono text-sm text-neutral-300 truncate pl-11">
                      {{ webhook.url }}
                    </div>

                    <!-- Events -->
                    <div class="flex flex-wrap items-center gap-1.5 pl-11">
                      <span
                        v-for="event in webhook.events"
                        :key="event"
                        :class="[
                          'inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[11px] font-medium',
                          eventColor(event),
                        ]"
                      >
                        <span :class="['w-1.5 h-1.5 rounded-full', eventDotColor(event)]" />
                        {{ event }}
                      </span>
                    </div>
                  </div>
                </div>

                <!-- Card toolbar -->
                <div class="flex items-center justify-between mt-4 pt-3 border-t border-neutral-800/50 pl-11">
                  <span class="text-xs text-neutral-600">
                    Created {{ new Date(webhook.createdAt).toLocaleDateString() }}
                  </span>
                  <div class="flex items-center gap-1">
                    <UButton
                      icon="i-lucide-send"
                      color="primary"
                      variant="ghost"
                      size="xs"
                      label="Test"
                      :loading="testingWebhookId === webhook.id"
                      :disabled="testingWebhookId !== null"
                      @click="handleTest(webhook.id)"
                    />
                    <UButton
                      :icon="webhook.isActive ? 'i-lucide-pause' : 'i-lucide-play'"
                      :color="webhook.isActive ? 'neutral' : 'primary'"
                      variant="ghost"
                      size="xs"
                      :label="webhook.isActive ? 'Pause' : 'Resume'"
                      @click="toggleActive(webhook)"
                    />
                    <UButton
                      icon="i-lucide-pencil"
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      label="Edit"
                      @click="openEdit(webhook)"
                    />
                    <UButton
                      icon="i-lucide-trash-2"
                      color="error"
                      variant="ghost"
                      size="xs"
                      @click="confirmDelete(webhook.id)"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Webhook Modal -->
    <UModal v-model:open="isEditModalOpen">
      <template #content>
        <div class="p-6 space-y-5">
          <div class="flex items-center gap-3">
            <div class="w-12 h-12 rounded-xl bg-primary-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-pencil" class="text-2xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Edit Webhook</h3>
              <p class="text-sm text-neutral-400">Update webhook configuration</p>
            </div>
          </div>

          <div class="flex flex-col gap-4">
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Endpoint URL</label>
              <UInput
                v-model="editUrl"
                placeholder="https://example.com/webhook"
              />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Signing Secret</label>
              <UInput
                v-model="editSecret"
                type="password"
                placeholder="Leave blank to keep current"
              />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Format</label>
              <USelect
                v-model="editFormat"
                :items="formatOptions"
                class="w-full min-w-[250px]"
              />
            </div>
            <div v-if="editFormat === 'discord' || editFormat === 'slack' || editFormat === 'teams'">
              <p class="text-xs font-medium text-neutral-400 mb-2">Custom Overrides</p>
              <div class="flex flex-col gap-2">
                <UInput
                  v-model="editCustomTitle"
                  placeholder="{{module.name}} v{{module.version}} published"
                />
                <UInput
                  v-model="editCustomBody"
                  placeholder="{{module.description}}"
                />
              </div>
            </div>
            <div v-if="editFormat === 'custom'">
              <p class="text-xs font-medium text-neutral-400 mb-2">Template Body</p>
              <UTextarea
                v-model="editTemplate"
                placeholder='{"text":"{{event}} - {{module.name}}"}'
                :rows="8"
                class="w-full font-mono text-sm"
              />
              <p class="text-xs text-neutral-500 mt-1">
                Available variables: {{ templateVariablesText }}
              </p>
            </div>
            <div>
              <p class="text-xs font-medium text-neutral-400 mb-2">Events</p>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="event in WEBHOOK_EVENTS"
                  :key="event"
                  type="button"
                  :class="[
                    'inline-flex items-center gap-2 px-3 py-1.5 rounded-lg border text-sm font-medium transition-all cursor-pointer',
                    editEvents.includes(event)
                      ? `${eventSelectedBg(event)}`
                      : `bg-neutral-900/40 ${eventBorderColor(event)} text-neutral-400`
                  ]"
                  @click="toggleEditEvent(event)"
                >
                  <span :class="['w-2 h-2 rounded-full', eventDotColor(event)]" />
                  <span :class="editEvents.includes(event) ? 'text-neutral-100' : ''">{{ event }}</span>
                </button>
              </div>
            </div>
          </div>

          <div class="flex justify-end gap-2 pt-2 border-t border-neutral-800/50">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isEditModalOpen = false"
            />
            <UButton
              color="primary"
              label="Save Changes"
              :disabled="!editUrl || editEvents.length === 0"
              @click="handleUpdate"
            />
          </div>
        </div>
      </template>
    </UModal>

    <!-- Delete Confirmation Modal -->
    <UModal v-model:open="isDeleteModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-red-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-trash-2" class="text-2xl text-red-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Delete Webhook</h3>
              <p class="text-sm text-neutral-400">This action cannot be undone</p>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            Are you sure you want to delete this webhook? It will no longer receive event notifications.
          </p>
          <div class="flex justify-end gap-2">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isDeleteModalOpen = false"
            />
            <UButton
              color="error"
              label="Delete Webhook"
              @click="handleDelete"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>

<style scoped>
.webhook-create-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.8), rgba(10, 10, 10, 0.9));
  backdrop-filter: blur(12px);
}

.webhook-list-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(15, 15, 15, 0.8));
  backdrop-filter: blur(8px);
}

.webhook-list-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 28, 0.7), rgba(18, 18, 18, 0.9));
}

.format-card:hover {
  transform: translateY(-1px);
}

.format-card:active {
  transform: translateY(0);
}

.slide-fade-enter-active,
.slide-fade-leave-active {
  transition: all 0.25s ease;
}

.slide-fade-enter-from,
.slide-fade-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}
</style>
