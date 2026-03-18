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
      <div class="max-w-4xl space-y-6">

        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl" />
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
        <div
          v-if="testResult"
          :class="[
            'p-4 rounded-xl flex items-center gap-3 transition-all',
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

        <!-- Create Webhook Form -->
        <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
          <h3 class="text-sm font-semibold mb-3 text-neutral-200 flex items-center gap-2">
            <UIcon name="i-lucide-plus-circle" class="text-primary-400" />
            Add Webhook
          </h3>
          <div class="flex flex-col gap-3">
            <UInput
              v-model="newUrl"
              placeholder="https://example.com/webhook"
              @keyup.enter="handleCreate"
            />
            <UInput
              v-model="newSecret"
              type="password"
              placeholder="Signing secret (optional)"
            />
            <div>
              <p class="text-xs text-neutral-400 mb-2">Format</p>
              <USelect
                v-model="newFormat"
                :items="formatOptions"
                class="w-full min-w-[250px]"
              />
            </div>
            <div v-if="newFormat === 'discord' || newFormat === 'slack' || newFormat === 'teams'">
              <p class="text-xs text-neutral-400 mb-2">Custom Overrides (optional)</p>
              <div class="flex flex-col gap-2">
                <UInput
                  v-model="newCustomTitle"
                  placeholder="{{module.name}} v{{module.version}} published"
                />
                <UInput
                  v-model="newCustomBody"
                  placeholder="{{module.description}}"
                />
              </div>
            </div>
            <div v-if="newFormat === 'custom'">
              <p class="text-xs text-neutral-400 mb-2">Template Body</p>
              <UTextarea
                v-model="newTemplate"
                placeholder='{"text":"{{event}} - {{module.name}}"}'
                :rows="8"
                class="w-full font-mono text-sm"
              />
              <p class="text-xs text-neutral-500 mt-1">
                Available variables: {{ templateVariablesText }}
              </p>
            </div>
            <div>
              <p class="text-xs text-neutral-400 mb-2">Events</p>
              <div class="flex flex-wrap gap-2">
                <label
                  v-for="event in WEBHOOK_EVENTS"
                  :key="event"
                  class="flex items-center gap-1.5 text-sm text-neutral-300 cursor-pointer"
                >
                  <input
                    type="checkbox"
                    :value="event"
                    v-model="newEvents"
                    class="accent-neutral-500 rounded"
                  />
                  {{ event }}
                </label>
              </div>
            </div>
            <div class="flex justify-end">
              <UButton
                label="Create Webhook"
                color="primary"
                :loading="isCreating"
                :disabled="!newUrl || newEvents.length === 0"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Webhooks List -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-webhook" class="text-primary-400" />
            Your Webhooks
          </h2>

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="webhooks.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No webhooks configured. Add one to start receiving event notifications.</p>
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="webhook in webhooks"
              :key="webhook.id"
              class="flex items-center justify-between p-4 rounded-xl bg-neutral-900/40 border border-neutral-800 hover:border-neutral-700 transition-colors"
            >
              <div class="min-w-0 flex-1">
                <div class="font-medium text-neutral-100 truncate font-mono text-sm">
                  {{ webhook.url }}
                </div>
                <div class="flex flex-wrap items-center gap-2 mt-2">
                  <span
                    :class="[
                      'px-2 py-0.5 rounded-full text-[11px] font-medium',
                      formatColor(webhook.format),
                    ]"
                  >
                    {{ formatLabel(webhook.format) }}
                  </span>
                  <span
                    v-for="event in webhook.events"
                    :key="event"
                    :class="[
                      'px-2 py-0.5 rounded-full text-[11px] font-medium',
                      eventColor(event),
                    ]"
                  >
                    {{ event }}
                  </span>
                </div>
                <div class="text-xs text-neutral-500 flex items-center gap-3 mt-2">
                  <span
                    :class="[
                      'flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px]',
                      webhook.isActive
                        ? 'bg-green-900/40 text-green-300'
                        : 'bg-neutral-800 text-neutral-400',
                    ]"
                  >
                    <UIcon
                      :name="webhook.isActive ? 'i-lucide-check-circle' : 'i-lucide-circle-off'"
                      class="text-[13px]"
                    />
                    {{ webhook.isActive ? "Active" : "Inactive" }}
                  </span>
                  <span>Created {{ new Date(webhook.createdAt).toLocaleDateString() }}</span>
                </div>
              </div>
              <div class="flex items-center gap-2 ml-4">
                <UButton
                  icon="i-lucide-send"
                  color="primary"
                  variant="ghost"
                  size="sm"
                  title="Send test"
                  :loading="testingWebhookId === webhook.id"
                  :disabled="testingWebhookId !== null"
                  @click="handleTest(webhook.id)"
                />
                <UButton
                  :icon="webhook.isActive ? 'i-lucide-pause' : 'i-lucide-play'"
                  :color="webhook.isActive ? 'neutral' : 'primary'"
                  variant="ghost"
                  size="sm"
                  :title="webhook.isActive ? 'Deactivate' : 'Activate'"
                  @click="toggleActive(webhook)"
                />
                <UButton
                  icon="i-lucide-pencil"
                  color="neutral"
                  variant="ghost"
                  size="sm"
                  title="Edit"
                  @click="openEdit(webhook)"
                />
                <UButton
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  size="sm"
                  title="Delete"
                  @click="confirmDelete(webhook.id)"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Webhook Modal -->
    <UModal v-model:open="isEditModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-primary-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-pencil" class="text-2xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Edit Webhook</h3>
              <p class="text-sm text-neutral-400">Update webhook configuration</p>
            </div>
          </div>
          <div class="flex flex-col gap-3 mb-6">
            <UInput
              v-model="editUrl"
              placeholder="https://example.com/webhook"
            />
            <UInput
              v-model="editSecret"
              type="password"
              placeholder="New signing secret (leave blank to keep)"
            />
            <div>
              <p class="text-xs text-neutral-400 mb-2">Format</p>
              <USelect
                v-model="editFormat"
                :items="formatOptions"
                class="w-full min-w-[250px]"
              />
            </div>
            <div v-if="editFormat === 'discord' || editFormat === 'slack' || editFormat === 'teams'">
              <p class="text-xs text-neutral-400 mb-2">Custom Overrides (optional)</p>
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
              <p class="text-xs text-neutral-400 mb-2">Template Body</p>
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
              <p class="text-xs text-neutral-400 mb-2">Events</p>
              <div class="flex flex-wrap gap-2">
                <label
                  v-for="event in WEBHOOK_EVENTS"
                  :key="event"
                  class="flex items-center gap-1.5 text-sm text-neutral-300 cursor-pointer"
                >
                  <input
                    type="checkbox"
                    :value="event"
                    v-model="editEvents"
                    class="accent-neutral-500 rounded"
                  />
                  {{ event }}
                </label>
              </div>
            </div>
          </div>
          <div class="flex justify-end gap-2">
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
