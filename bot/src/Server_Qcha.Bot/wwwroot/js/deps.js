// 统一导出全局引入的 Vue 3 与 Element Plus API，方便各 ES Module 按需解构
export const { createApp, ref, reactive, computed, nextTick, watch, onMounted, onUnmounted } = window.Vue;
export const { ElMessage, ElMessageBox } = window.ElementPlus;
