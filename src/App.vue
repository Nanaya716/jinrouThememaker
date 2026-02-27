<script setup>
import CoffeeThemeMaker from './components/CoffeeThemeMaker.vue';

document.title = 'Coffee 主题文件生成器';
</script>

<template>
  <div class="wrapper">
    <CoffeeThemeMaker />
  </div>
</template>

<style>
.wrapper {
  width: 100%;
  min-height: 100vh;
}
</style>
