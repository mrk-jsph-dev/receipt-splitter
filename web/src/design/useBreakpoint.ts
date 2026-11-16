import { onScopeDispose, readonly, ref } from 'vue';

// Same numbers as breakpoints.css. Only for behaviour CSS can't do: whether an overlay is a
// bottom sheet, a dialog or a drawer, and whether drag and drop is on. Layout stays in CSS.

export type Size = 'compact' | 'medium' | 'expanded';

const queries = {
  medium: '(min-width: 640px)',
  expanded: '(min-width: 1024px)',
  // Drag and drop needs a mouse or trackpad and room for the people column next to the items.
  drag: '(min-width: 1024px) and (pointer: fine)',
} as const;

function matches(query: string): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(query).matches;
}

function current(): { size: Size; canDrag: boolean } {
  const size: Size = matches(queries.expanded) ? 'expanded' : matches(queries.medium) ? 'medium' : 'compact';
  return { size, canDrag: matches(queries.drag) };
}

export function useBreakpoint() {
  const size = ref<Size>(current().size);
  const canDrag = ref(current().canDrag);
  const update = () => {
    const now = current();
    size.value = now.size;
    canDrag.value = now.canDrag;
  };

  const lists = typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? Object.values(queries).map((q) => window.matchMedia(q))
    : [];
  lists.forEach((list) => list.addEventListener?.('change', update));
  onScopeDispose(() => lists.forEach((list) => list.removeEventListener?.('change', update)));

  return { size: readonly(size), canDrag: readonly(canDrag) };
}

/** Whether the user asked for less motion. Checked when an overlay opens. */
export const prefersReducedMotion = () => matches('(prefers-reduced-motion: reduce)');
