import { ref } from '../deps.js';
import { api } from '../api.js';
import { ElMessage } from '../deps.js';

const audit = ref([]);
const loadingAudit = ref(false);

async function loadAudit() {
  loadingAudit.value = true;
  try {
    audit.value = await api('/audit?limit=300');
  } catch (e) {
    ElMessage.error(e.message);
  } finally {
    loadingAudit.value = false;
  }
}

export function useAudit() {
  return {
    audit,
    loadingAudit,
    loadAudit,
  };
}
