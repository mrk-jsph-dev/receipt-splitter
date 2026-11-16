<template>
  <!-- Keyed by path, so moving from one bill to another never shows the first bill's data. -->
  <RouterView :key="$route.path" />
</template>
