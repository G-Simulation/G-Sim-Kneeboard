/*! Version: 0.6.1
Date: 2018-04-30 */

L.Control.GroupedLayers = L.Control.extend({
  options: {
    collapsed: true,
    position: "topright",
    autoZIndex: true,
    exclusiveGroups: [],
    groupCheckboxes: false
  },

  initialize: function(baseLayers, overlays, options) {
    var i, j;
    L.Util.setOptions(this, options);
    this._layers = [];
    this._lastZIndex = 0;
    this._handlingClick = false;
    this._groupList = [];
    this._domGroups = [];

    for (i in baseLayers) {
      this._addLayer(baseLayers[i], i);
    }

    for (i in overlays) {
      for (j in overlays[i]) {
        this._addLayer(overlays[i][j], j, i, true);
      }
    }
  },

  onAdd: function(map) {
    this._initLayout();
    this._update();
    map.on("layeradd", this._onLayerChange, this)
       .on("layerremove", this._onLayerChange, this);
    return this._container;
  },

  onRemove: function(map) {
    map.off("layeradd", this._onLayerChange, this)
       .off("layerremove", this._onLayerChange, this);
  },

  addBaseLayer: function(layer, name) {
    this._addLayer(layer, name);
    this._update();
    return this;
  },

  addOverlay: function(layer, name, group) {
    this._addLayer(layer, name, group, true);
    this._update();
    return this;
  },

  removeLayer: function(layer) {
    var id = L.Util.stamp(layer);
    var layerObj = this._getLayer(id);
    if (layerObj) {
      delete this._layers[this._layers.indexOf(layerObj)];
    }
    this._update();
    return this;
  },

  _getLayer: function(id) {
    for (var i = 0; i < this._layers.length; i++) {
      if (this._layers[i] && L.stamp(this._layers[i].layer) === id) {
        return this._layers[i];
      }
    }
  },

  _initLayout: function() {
    var className = "leaflet-control-layers";
    var container = this._container = L.DomUtil.create("div", className);
    container.setAttribute("aria-haspopup", true);

    if (L.Browser.touch) {
      L.DomEvent.on(container, "click", L.DomEvent.stopPropagation);
    } else {
      L.DomEvent.disableClickPropagation(container);
      L.DomEvent.on(container, "wheel", L.DomEvent.stopPropagation);
    }

    var form = this._form = L.DomUtil.create("form", className + "-list");

    if (this.options.collapsed) {
      if (!L.Browser.android) {
        L.DomEvent.on(container, "mouseover", this._expand, this)
                  .on(container, "mouseout", this._collapse, this);
      }

      var link = this._layersLink = L.DomUtil.create("a", className + "-toggle", container);
      link.href = "#";
      link.title = "Layers";

      if (L.Browser.touch) {
        L.DomEvent.on(link, "click", L.DomEvent.stop)
                  .on(link, "click", this._expand, this);
      } else {
        L.DomEvent.on(link, "focus", this._expand, this);
      }

      this._map.on("click", this._collapse, this);
    } else {
      this._expand();
    }

    this._baseLayersList = L.DomUtil.create("div", className + "-base", form);
    this._separator = L.DomUtil.create("div", className + "-separator", form);
    this._overlaysList = L.DomUtil.create("div", className + "-overlays", form);

    container.appendChild(form);
  },

  _addLayer: function(layer, name, group, overlay) {
    var id = L.Util.stamp(layer);
    var layerObj = {
      layer: layer,
      name: name,
      overlay: overlay
    };
    this._layers.push(layerObj);

    group = group || "";
    var groupId = this._indexOf(this._groupList, group);
    if (groupId === -1) {
      groupId = this._groupList.push(group) - 1;
    }

    var exclusive = this._indexOf(this.options.exclusiveGroups, group) !== -1;
    layerObj.group = {
      name: group,
      id: groupId,
      exclusive: exclusive
    };

    if (this.options.autoZIndex && layer.setZIndex) {
      this._lastZIndex++;
      layer.setZIndex(this._lastZIndex);
    }
  },

  _update: function() {
    if (!this._container) {
      return;
    }

    this._baseLayersList.innerHTML = "";
    this._overlaysList.innerHTML = "";
    this._domGroups.length = 0;

    var baseLayersPresent = false;
    var overlaysPresent = false;

    for (var i = 0; i < this._layers.length; i++) {
      var obj = this._layers[i];
      this._addItem(obj);
      overlaysPresent = overlaysPresent || obj.overlay;
      baseLayersPresent = baseLayersPresent || !obj.overlay;
    }

    this._separator.style.display = overlaysPresent && baseLayersPresent ? "" : "none";
  },

  _onLayerChange: function(e) {
    var obj = this._getLayer(L.Util.stamp(e.layer));
    if (!obj) {
      return;
    }

    if (!this._handlingClick) {
      this._update();
    }

    var type = obj.overlay
      ? (e.type === "layeradd" ? "overlayadd" : "overlayremove")
      : (e.type === "layeradd" ? "baselayerchange" : null);

    if (type) {
      this._map.fire(type, obj);
    }
  },

  _createRadioElement: function(name, checked) {
    var radioHtml = '<input type="radio" class="leaflet-control-layers-selector" name="' + name + '"';
    if (checked) {
      radioHtml += ' checked="checked"';
    }
    radioHtml += "/>";

    var radioFragment = document.createElement("div");
    radioFragment.innerHTML = radioHtml;
    return radioFragment.firstChild;
  },

  _addItem: function(obj) {
    var label = document.createElement("label");
    var input;
    var checked = this._map.hasLayer(obj.layer);

    if (obj.overlay) {
      if (obj.group.exclusive) {
        var groupName = "leaflet-exclusive-group-layer-" + obj.group.id;
        input = this._createRadioElement(groupName, checked);
      } else {
        input = document.createElement("input");
        input.type = "checkbox";
        input.className = "leaflet-control-layers-selector";
        input.defaultChecked = checked;
      }
    } else {
      input = this._createRadioElement("leaflet-base-layers", checked);
    }

    input.layerId = L.Util.stamp(obj.layer);
    input.groupID = obj.group.id;
    L.DomEvent.on(input, "click", this._onInputClick, this);

    var name = document.createElement("span");
    name.innerHTML = " " + obj.name;

    label.appendChild(input);
    label.appendChild(name);

    var container;
    if (obj.overlay) {
      container = this._overlaysList;

      var groupContainer = this._domGroups[obj.group.id];
      if (!groupContainer) {
        groupContainer = document.createElement("div");
        groupContainer.className = "leaflet-control-layers-group";
        groupContainer.id = "leaflet-control-layers-group-" + obj.group.id;

        var groupLabel = document.createElement("label");
        groupLabel.className = "leaflet-control-layers-group-label";

        if (obj.group.name !== "" && !obj.group.exclusive && this.options.groupCheckboxes) {
          var groupCheckbox = document.createElement("input");
          groupCheckbox.type = "checkbox";
          groupCheckbox.className = "leaflet-control-layers-group-selector";
          groupCheckbox.groupID = obj.group.id;
          groupCheckbox.legend = this;
          L.DomEvent.on(groupCheckbox, "click", this._onGroupInputClick, groupCheckbox);
          groupLabel.appendChild(groupCheckbox);
        }

        var groupName = document.createElement("span");
        groupName.className = "leaflet-control-layers-group-name";
        groupName.innerHTML = obj.group.name;
        groupLabel.appendChild(groupName);

        groupContainer.appendChild(groupLabel);
        container.appendChild(groupContainer);
        this._domGroups[obj.group.id] = groupContainer;
      }

      container = groupContainer;
    } else {
      container = this._baseLayersList;
    }

    container.appendChild(label);
    return label;
  },

  _onGroupInputClick: function() {
    var legend = this.legend;
    legend._handlingClick = true;

    var inputs = legend._form.getElementsByTagName("input");
    var inputsLen = inputs.length;

    for (var i = 0; i < inputsLen; i++) {
      var input = inputs[i];
      if (input.groupID === this.groupID && input.className === "leaflet-control-layers-selector") {
        input.checked = this.checked;
        var obj = legend._getLayer(input.layerId);
        if (input.checked && !legend._map.hasLayer(obj.layer)) {
          legend._map.addLayer(obj.layer);
        } else if (!input.checked && legend._map.hasLayer(obj.layer)) {
          legend._map.removeLayer(obj.layer);
        }
      }
    }

    legend._handlingClick = false;
  },

  _onInputClick: function() {
    var inputs = this._form.getElementsByTagName("input");
    var inputsLen = inputs.length;

    this._handlingClick = true;

    for (var i = 0; i < inputsLen; i++) {
      var input = inputs[i];
      if (input.className === "leaflet-control-layers-selector") {
        var obj = this._getLayer(input.layerId);
        if (input.checked && !this._map.hasLayer(obj.layer)) {
          this._map.addLayer(obj.layer);
        } else if (!input.checked && this._map.hasLayer(obj.layer)) {
          this._map.removeLayer(obj.layer);
        }
      }
    }

    this._handlingClick = false;
  },

  _expand: function() {
    L.DomUtil.addClass(this._container, "leaflet-control-layers-expanded");
    var acceptableHeight = this._map._size.y - (this._container.offsetTop * 4);
    if (acceptableHeight < this._form.clientHeight) {
      L.DomUtil.addClass(this._form, "leaflet-control-layers-scrollbar");
      this._form.style.height = acceptableHeight + "px";
    }
  },

  _collapse: function() {
    this._container.className = this._container.className.replace(" leaflet-control-layers-expanded", "");
  },

  _indexOf: function(arr, obj) {
    for (var i = 0, len = arr.length; i < len; i++) {
      if (arr[i] === obj) {
        return i;
      }
    }
    return -1;
  }
});

L.control.groupedLayers = function(baseLayers, overlays, options) {
  return new L.Control.GroupedLayers(baseLayers, overlays, options);
};
