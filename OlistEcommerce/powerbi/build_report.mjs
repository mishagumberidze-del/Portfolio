import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const reportRoot = path.resolve('powerbi/OlistEcommercePBIP/OlistEcommerce.Report');
const definitionRoot = path.join(reportRoot, 'definition');
const pagesRoot = path.join(definitionRoot, 'pages');

if (!pagesRoot.startsWith(reportRoot + path.sep)) {
  throw new Error(`Unsafe report pages path: ${pagesRoot}`);
}

const schema = {
  page: 'https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.1.0/schema.json',
  visual: 'https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.9.0/schema.json'
};

const colors = {
  navy: '#18324A',
  teal: '#136F63',
  gold: '#E09F3E',
  blue: '#2F6690',
  red: '#9E2A2B',
  ink: '#17202A',
  muted: '#52616B',
  canvas: '#F6F8FA',
  white: '#FFFFFF',
  border: '#DCE4EA'
};

const ids = {
  executive: 'ccb18bcc8e220679216e',
  customer: '6f5d372b977ab3dd6414',
  product: '37627a28c5ec20819f6a',
  delivery: '8fd5ab16a03ec65df0d7',
  seller: '4637d708bf201d194c0c',
  customerDetail: 'b54b276586287190d3c1'
};

const lit = (value) => ({ expr: { Literal: { Value: value } } });
const colorExpr = (value) => ({ solid: { color: lit(`'${value}'`) } });
const visualId = (pageId, key) => crypto.createHash('sha1').update(`${pageId}:${key}`).digest('hex').slice(0, 20);

function column(entity, property) {
  return {
    field: { Column: { Expression: { SourceRef: { Entity: entity } }, Property: property } },
    queryRef: `${entity}.${property}`,
    nativeQueryRef: property
  };
}

function measure(property) {
  return {
    field: { Measure: { Expression: { SourceRef: { Entity: 'KPI Measures' } }, Property: property } },
    queryRef: `KPI Measures.${property}`,
    nativeQueryRef: property
  };
}

function position(x, y, width, height, z) {
  return { x, y, z, height, width, tabOrder: z };
}

function chrome(title, { background = true, border = true, padding = 8 } = {}) {
  return {
    title: [{ properties: {
      show: lit('true'),
      text: lit(`'${title}'`),
      fontColor: colorExpr(colors.ink),
      fontSize: lit('12D')
    } }],
    subTitle: [{ properties: { show: lit('false') } }],
    background: [{ properties: {
      show: lit(background ? 'true' : 'false'),
      color: colorExpr(colors.white),
      transparency: lit('0D')
    } }],
    border: [{ properties: {
      show: lit(border ? 'true' : 'false'),
      color: colorExpr(colors.border),
      radius: lit('8D'),
      width: lit('1D')
    } }],
    visualHeader: [{ properties: { show: lit('false') } }],
    padding: [{ properties: {
      top: lit(`${padding}D`), bottom: lit(`${padding}D`),
      left: lit(`${padding}D`), right: lit(`${padding}D`)
    } }]
  };
}

function baseVisual(pageId, key, visualType, x, y, width, height, title, query, objects = {}) {
  return {
    $schema: schema.visual,
    name: visualId(pageId, key),
    position: position(x, y, width, height, 1000 + Object.keys(objects).length + Math.abs(x + y)),
    visual: {
      visualType,
      ...(query ? { query } : {}),
      ...(Object.keys(objects).length ? { objects } : {}),
      visualContainerObjects: chrome(title)
    }
  };
}

function headerVisuals(pageId, section, title, subtitle) {
  const shapeName = visualId(pageId, 'header-shape');
  const textName = visualId(pageId, 'header-text');
  const shape = {
    $schema: schema.visual,
    name: shapeName,
    position: position(0, 0, 1920, 70, 1),
    visual: {
      visualType: 'shape',
      objects: {
        shape: [{ properties: { tileShape: lit("'rectangle'") } }],
        fill: [{ properties: { fillColor: colorExpr(colors.navy), transparency: lit('0D') }, selector: { id: 'default' } }],
        outline: [{ properties: { show: lit('false') }, selector: { id: 'default' } }]
      },
      visualContainerObjects: chrome('', { background: false, border: false, padding: 0 })
    }
  };
  shape.visual.visualContainerObjects.title[0].properties.show = lit('false');

  const text = {
    $schema: schema.visual,
    name: textName,
    position: position(24, 10, 1500, 52, 2),
    visual: {
      visualType: 'textbox',
      objects: {
        general: [{ properties: { paragraphs: [{
          textRuns: [
            { value: `${section}  ${title}`, textStyle: { fontFamily: 'Segoe UI Semibold', fontSize: '24px', color: colors.white } },
            { value: `   ${subtitle}`, textStyle: { fontFamily: 'Segoe UI', fontSize: '12px', color: '#C8D6E2' } }
          ],
          horizontalTextAlignment: 'left'
        }] } }]
      },
      visualContainerObjects: chrome('', { background: false, border: false, padding: 0 })
    }
  };
  text.visual.visualContainerObjects.title[0].properties.show = lit('false');
  return [shape, text];
}

function slicer(pageId, key, entity, property, label, x, y, width = 220, defaultValue) {
  const query = { queryState: { Values: { projections: [column(entity, property)] } } };
  const objects = {
    data: [{ properties: { mode: lit("'Dropdown'") } }],
    header: [{ properties: { show: lit('true'), text: lit(`'${label}'`) } }]
  };
  if (defaultValue !== undefined) {
    const source = 's';
    const literalValue = typeof defaultValue === 'number' ? `${defaultValue}L` : `'${defaultValue}'`;
    const selectedColumn = { Column: { Expression: { SourceRef: { Source: source } }, Property: property } };
    objects.general = [{ properties: {
      filter: {
        filter: {
          Version: 2,
          From: [{ Name: source, Entity: entity, Type: 0 }],
          Where: [{
            Condition: { In: { Expressions: [selectedColumn], Values: [[{ Literal: { Value: literalValue } }]] } },
            Annotations: {
              filterExpressionMetadata: { expressions: [selectedColumn] },
              decomposedIdentities: {
                values: [[{ 0: [{ Literal: { Value: literalValue } }] }]],
                columns: [{ value: selectedColumn }]
              },
              valueMap: [{ 0: String(defaultValue) }]
            }
          }]
        }
      }
    } }];
  }
  const visual = baseVisual(pageId, key, 'slicer', x, y, width, 80, '', query, objects);
  visual.visual.visualContainerObjects.title[0].properties.show = lit('false');
  return visual;
}

function card(pageId, key, measures, title, x, y, width, height = 140) {
  const query = { queryState: { Data: { projections: measures.map(measure) } } };
  const objects = {
    outline: [{ properties: { show: lit('false') }, selector: { id: 'default' } }],
    value: [{ properties: { fontSize: lit('23D'), bold: lit('true'), fontColor: colorExpr(colors.ink) }, selector: { id: 'default' } }],
    label: [{ properties: { show: lit('true'), fontSize: lit('11D'), fontColor: colorExpr(colors.muted) }, selector: { id: 'default' } }],
    cardCalloutArea: [{ properties: { show: lit('true'), paddingUniform: lit('8L'), rectangleRoundedCurve: lit('6L'), backgroundFillColor: colorExpr('#F9FBFC'), backgroundTransparency: lit('0D') } }],
    layout: [{ properties: { style: lit("'Table'"), customizeLines: lit('true'), gridlineWidth: lit('1D'), gridlineColor: colorExpr(colors.border), gridlineTransparency: lit('0D'), gridlineStyle: lit("'solid'") }, selector: { id: 'default' } }]
  };
  return baseVisual(pageId, key, 'cardVisual', x, y, width, height, title, query, objects);
}

function chart(pageId, key, visualType, category, values, title, x, y, width, height, options = {}) {
  const queryState = {
    Category: { projections: [column(category.entity, category.property)] },
    Y: { projections: values.map(measure) }
  };
  if (options.series) queryState.Series = { projections: [column(options.series.entity, options.series.property)] };
  if (options.tooltips?.length) queryState.Tooltips = { projections: options.tooltips.map(measure) };
  const query = { queryState };
  if (options.sortMeasure) {
    query.sortDefinition = { sort: [{ field: measure(options.sortMeasure).field, direction: options.sortDirection || 'Descending' }], isDefaultSort: false };
  }
  const objects = {};
  return baseVisual(pageId, key, visualType, x, y, width, height, title, query, objects);
}

function donut(pageId, key, category, value, title, x, y, width, height) {
  const query = { queryState: {
    Category: { projections: [column(category.entity, category.property)] },
    Y: { projections: [measure(value)] },
    Tooltips: { projections: [measure('Sales / GMV'), measure('Average Review Score')] }
  } };
  return baseVisual(pageId, key, 'donutChart', x, y, width, height, title, query, {});
}

function topNBySumFilter(pageId, key, entity, property, valueEntity, valueProperty, top) {
  const categoryAlias = 'category';
  const valueAlias = 'value';
  return {
    filters: [{
      name: `Filter${visualId(pageId, `${key}-topn`).slice(0, 24)}`,
      field: column(entity, property).field,
      type: 'TopN',
      filter: {
        Version: 2,
        From: [
          {
            Name: 'subquery',
            Expression: {
              Subquery: {
                Query: {
                  Version: 2,
                  From: [
                    { Name: categoryAlias, Entity: entity, Type: 0 },
                    { Name: valueAlias, Entity: valueEntity, Type: 0 }
                  ],
                  Select: [{
                    Column: { Expression: { SourceRef: { Source: categoryAlias } }, Property: property },
                    Name: 'field'
                  }],
                  OrderBy: [{
                    Direction: 2,
                    Expression: {
                      Aggregation: {
                        Expression: { Column: { Expression: { SourceRef: { Source: valueAlias } }, Property: valueProperty } },
                        Function: 0
                      }
                    }
                  }],
                  Top: top
                }
              }
            },
            Type: 2
          },
          { Name: categoryAlias, Entity: entity, Type: 0 }
        ],
        Where: [{
          Condition: {
            In: {
              Expressions: [{ Column: { Expression: { SourceRef: { Source: categoryAlias } }, Property: property } }],
              Table: { SourceRef: { Source: 'subquery' } }
            }
          }
        }]
      },
      howCreated: 'User'
    }]
  };
}

function table(pageId, key, fields, title, x, y, width, height, sortMeasure, options = {}) {
  const projections = fields.map((field) => field.type === 'measure' ? measure(field.property) : column(field.entity, field.property));
  const query = { queryState: { Values: { projections } } };
  if (sortMeasure) query.sortDefinition = { sort: [{ field: measure(sortMeasure).field, direction: 'Descending' }], isDefaultSort: false };
  const objects = {
    columnHeaders: [{ properties: { columnAdjustment: lit("'growToFit'"), autoSizeColumnWidth: lit('true') } }],
    values: [{ properties: { backColorSecondary: colorExpr('#F4F7F9') } }]
  };
  const visual = baseVisual(pageId, key, 'tableEx', x, y, width, height, title, query, objects);
  if (options.topNBySum) {
    visual.filterConfig = topNBySumFilter(pageId, key, ...options.topNBySum);
  }
  return visual;
}

function matrix(pageId, key, row, col, value, title, x, y, width, height) {
  const query = { queryState: {
    Rows: { projections: [column(row.entity, row.property)] },
    Columns: { projections: [column(col.entity, col.property)] },
    Values: { projections: [measure(value)] }
  } };
  const objects = {
    columnHeaders: [{ properties: { columnAdjustment: lit("'growToFit'"), autoSizeColumnWidth: lit('true') } }],
    values: [{ properties: { backColorSecondary: colorExpr('#E9F2F0') } }],
    subTotals: [{ properties: { rowSubtotals: lit('false'), columnSubtotals: lit('false') } }]
  };
  return baseVisual(pageId, key, 'pivotTable', x, y, width, height, title, query, objects);
}

function page(id, displayName, visuals, extra = {}) {
  const pageDir = path.join(pagesRoot, id);
  const visualsDir = path.join(pageDir, 'visuals');
  const resolvedVisualsDir = path.resolve(visualsDir);
  if (!resolvedVisualsDir.startsWith(pagesRoot + path.sep)) throw new Error(`Unsafe visuals path: ${resolvedVisualsDir}`);
  fs.rmSync(resolvedVisualsDir, { recursive: true, force: true });
  fs.mkdirSync(resolvedVisualsDir, { recursive: true });
  const pageJson = {
    $schema: schema.page,
    name: id,
    displayName,
    displayOption: 'FitToPage',
    height: 1080,
    width: 1920,
    ...extra
  };
  fs.writeFileSync(path.join(pageDir, 'page.json'), JSON.stringify(pageJson, null, 2) + '\n');
  for (const visual of visuals) {
    const dir = path.join(visualsDir, visual.name);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'visual.json'), JSON.stringify(visual, null, 2) + '\n');
  }
}

const executive = [
  ...headerVisuals(ids.executive, '01', 'Executive Overview', 'Customer & e-commerce performance'),
  slicer(ids.executive, 'year', 'Date', 'Year', 'Year', 24, 82, 180),
  slicer(ids.executive, 'state', 'Customer Geography', 'State', 'Customer state', 216, 82, 230),
  slicer(ids.executive, 'status', 'Order', 'Status', 'Order status', 458, 82, 230),
  slicer(ids.executive, 'metric', 'Metric Selector', 'Metric', 'Trend metric', 700, 82, 260, 'Sales / GMV'),
  card(ids.executive, 'commercial-kpis', ['Sales / GMV', 'Orders', 'Customers', 'Average Order Value', 'Items per Order'], 'Commercial health', 24, 174, 1872, 140),
  card(ids.executive, 'experience-kpis', ['Repeat Customer Rate', 'Delivery Rate', 'Cancellation Rate', 'Average Delivery Days', 'On-Time Delivery %', 'Average Review Score'], 'Customer & fulfilment health', 24, 324, 1872, 140),
  chart(ids.executive, 'trend', 'lineChart', { entity: 'Date', property: 'Year Month' }, ['Selected Metric'], 'Dynamic monthly trend', 24, 476, 1150, 300, { tooltips: ['Sales / GMV', 'Orders', 'Customers', 'Sales MoM %'] }),
  table(ids.executive, 'status-mix', [
    { entity: 'Order', property: 'Status' },
    { type: 'measure', property: 'Orders' },
    { type: 'measure', property: 'Order Status Share %' }
  ], 'Order status breakdown', 1190, 476, 706, 300, 'Orders'),
  chart(ids.executive, 'state-sales', 'clusteredBarChart', { entity: 'Customer Geography', property: 'State' }, ['Sales / GMV'], 'Sales by customer state', 24, 790, 1872, 266, { sortMeasure: 'Sales / GMV', tooltips: ['Orders', 'Customers', 'Average Delivery Days', 'Average Review Score'] })
];

const customer = [
  ...headerVisuals(ids.customer, '02', 'Customer Deep Dive', 'Value, repeat behavior, RFM and retention'),
  slicer(ids.customer, 'type', 'Customer', 'Customer Type', 'Customer type', 24, 82, 220),
  slicer(ids.customer, 'rfm', 'Customer', 'RFM Segment', 'RFM segment', 256, 82, 260),
  slicer(ids.customer, 'state', 'Customer', 'State', 'Home state', 528, 82, 210),
  card(ids.customer, 'customer-kpis', ['Customers', 'Repeat Customers', 'Repeat Customer Rate', 'Customer Purchase Frequency', 'Revenue per Customer'], 'Customer economics', 24, 174, 1872, 140),
  chart(ids.customer, 'new-repeat', 'columnChart', { entity: 'Date', property: 'Year Month' }, ['Sales / GMV'], 'New vs repeat customer sales', 24, 326, 910, 300, { series: { entity: 'Order', property: 'Customer Order Type' }, tooltips: ['Customers', 'Orders', 'Average Order Value'] }),
  chart(ids.customer, 'rfm-sales', 'clusteredBarChart', { entity: 'Customer', property: 'RFM Segment' }, ['Sales / GMV'], 'Revenue by RFM segment', 950, 326, 946, 300, { sortMeasure: 'Sales / GMV', tooltips: ['Customers', 'Repeat Customer Rate', 'Revenue per Customer'] }),
  matrix(ids.customer, 'cohort', { entity: 'Cohort Retention', property: 'Cohort Month' }, { entity: 'Cohort Retention', property: 'Months Since First Purchase' }, 'Retention Rate', 'Monthly customer retention cohort', 24, 640, 1080, 416),
  table(ids.customer, 'top-customers', [
    { entity: 'Customer', property: 'Customer Display ID' },
    { entity: 'Customer', property: 'RFM Segment' },
    { entity: 'Customer', property: 'Purchase Count' },
    { entity: 'Customer', property: 'First Purchase' },
    { entity: 'Customer', property: 'Last Purchase' },
    { type: 'measure', property: 'Sales / GMV' },
    { type: 'measure', property: 'Average Review Score' }
  ], 'Customer value table · right-click a customer to drill through', 1120, 640, 776, 416, 'Sales / GMV')
];

const product = [
  ...headerVisuals(ids.product, '03', 'Product & Sales', 'Category contribution and product demand'),
  slicer(ids.product, 'category', 'Product', 'Category', 'Category', 24, 82, 280),
  slicer(ids.product, 'year', 'Date', 'Year', 'Year', 316, 82, 180),
  card(ids.product, 'product-kpis', ['Sales / GMV', 'Units', 'Product Orders', 'Average Selling Price', 'Customers in Item Context'], 'Product performance', 24, 174, 1872, 140),
  chart(ids.product, 'category-sales', 'clusteredBarChart', { entity: 'Product', property: 'Category' }, ['Sales / GMV'], 'Category sales contribution', 24, 326, 910, 340, { sortMeasure: 'Sales / GMV', tooltips: ['Units', 'Product Orders', 'Average Selling Price', 'Category Contribution %'] }),
  chart(ids.product, 'category-units', 'clusteredBarChart', { entity: 'Product', property: 'Category' }, ['Units'], 'Category unit demand', 950, 326, 946, 340, { sortMeasure: 'Units', tooltips: ['Sales / GMV', 'Product Orders', 'Customers in Item Context'] }),
  table(ids.product, 'products', [
    { entity: 'Product', property: 'Product Display ID' },
    { entity: 'Product', property: 'Category' },
    { type: 'measure', property: 'Units' },
    { type: 'measure', property: 'Product Orders' },
    { type: 'measure', property: 'Sales / GMV' },
    { type: 'measure', property: 'Average Selling Price' },
    { type: 'measure', property: 'Customers in Item Context' },
    { type: 'measure', property: 'Average Review Score in Item Context' }
  ], 'Top 25 products by sales', 24, 680, 1872, 376, 'Sales / GMV', {
    topNBySum: ['Product', 'Product ID', 'Order Item', 'Item Gross Value', 25]
  })
];

const delivery = [
  ...headerVisuals(ids.delivery, '04', 'Delivery & Customer Experience', 'Promise reliability, freight and review outcomes'),
  slicer(ids.delivery, 'performance', 'Order', 'Delivery Performance', 'Delivery result', 24, 82, 250),
  slicer(ids.delivery, 'status', 'Order', 'Status', 'Order status', 286, 82, 220),
  slicer(ids.delivery, 'review', 'Review', 'Review Score Group', 'Review group', 518, 82, 250),
  card(ids.delivery, 'delivery-kpis', ['Average Delivery Days', 'On-Time Delivery %', 'Average Delay Days', 'Freight Cost', 'Freight Ratio', 'Average Review Score'], 'Delivery experience', 24, 174, 1872, 140),
  donut(ids.delivery, 'promise', { entity: 'Order', property: 'Delivery Performance' }, 'Orders', 'Delivery outcome mix', 24, 326, 600, 340),
  chart(ids.delivery, 'delay-review', 'clusteredColumnChart', { entity: 'Review', property: 'Review Score Group' }, ['Average Delivery Days in Review Context', 'Average Delay Days in Review Context'], 'Delivery timing by review outcome', 640, 326, 620, 340, { tooltips: ['Reviewed Orders', 'Average Review Score', 'On-Time Delivery % in Review Context'] }),
  chart(ids.delivery, 'freight-state', 'clusteredBarChart', { entity: 'Customer Geography', property: 'State' }, ['Freight Ratio'], 'Freight burden by customer state', 1276, 326, 620, 340, { sortMeasure: 'Freight Ratio', tooltips: ['Freight Cost', 'Sales / GMV', 'Orders'] }),
  chart(ids.delivery, 'delivery-trend', 'lineChart', { entity: 'Date', property: 'Year Month' }, ['Average Delivery Days', 'Average Delay Days'], 'Delivery trend over time', 24, 680, 1150, 376, { tooltips: ['On-Time Delivery %', 'Average Review Score', 'Orders'] }),
  donut(ids.delivery, 'reviews', { entity: 'Review', property: 'Review Score Group' }, 'Reviewed Orders', 'Review score mix', 1190, 680, 706, 376)
];

const seller = [
  ...headerVisuals(ids.seller, '05', 'Seller & Geographic Performance', 'Seller scale, fulfilment and regional reach'),
  slicer(ids.seller, 'state', 'Seller Geography', 'State', 'Seller state', 24, 82, 220),
  slicer(ids.seller, 'topn', 'Top N', 'Value', 'Top N sellers', 256, 82, 190, 10),
  card(ids.seller, 'seller-kpis', ['Active Sellers', 'Sales / GMV', 'Seller Orders', 'Seller Average Order Value', 'On-Time Delivery % in Item Context', 'Average Review Score in Item Context'], 'Seller network', 24, 174, 1872, 140),
  chart(ids.seller, 'seller-sales', 'clusteredBarChart', { entity: 'Seller', property: 'Seller Display ID' }, ['Seller Top N Sales'], 'Seller sales ranking', 24, 326, 910, 340, { sortMeasure: 'Seller Top N Sales', tooltips: ['Seller Orders', 'Customers in Item Context', 'On-Time Delivery % in Item Context', 'Average Review Score in Item Context'] }),
  chart(ids.seller, 'state-sales', 'clusteredBarChart', { entity: 'Seller Geography', property: 'State' }, ['Sales / GMV'], 'Seller sales by state', 950, 326, 946, 340, { sortMeasure: 'Sales / GMV', tooltips: ['Active Sellers', 'Seller Orders', 'Average Delivery Days in Item Context'] }),
  table(ids.seller, 'seller-table', [
    { entity: 'Seller', property: 'Seller Display ID' },
    { entity: 'Seller Geography', property: 'State' },
    { entity: 'Seller Geography', property: 'City' },
    { type: 'measure', property: 'Sales / GMV' },
    { type: 'measure', property: 'Seller Orders' },
    { type: 'measure', property: 'Customers in Item Context' },
    { type: 'measure', property: 'Average Delivery Days in Item Context' },
    { type: 'measure', property: 'On-Time Delivery % in Item Context' },
    { type: 'measure', property: 'Average Review Score in Item Context' }
  ], 'Seller performance table', 24, 680, 1872, 376, 'Sales / GMV')
];

const drillFilter = 'Filterfceee2c9f9ee6e2ea0fc741b';
const customerDetail = [
  ...headerVisuals(ids.customerDetail, 'DETAIL', 'Customer Drill-through', 'Customer identity, value, purchase history and experience'),
  card(ids.customerDetail, 'identity', ['Sales / GMV', 'Orders', 'Average Order Value', 'Customer Purchase Frequency', 'Average Review Score'], 'Selected customer summary', 24, 94, 1872, 140),
  table(ids.customerDetail, 'profile', [
    { entity: 'Customer', property: 'Customer Display ID' },
    { entity: 'Customer', property: 'Customer Type' },
    { entity: 'Customer', property: 'RFM Segment' },
    { entity: 'Customer', property: 'State' },
    { entity: 'Customer', property: 'City' },
    { entity: 'Customer', property: 'First Purchase' },
    { entity: 'Customer', property: 'Last Purchase' },
    { entity: 'Customer', property: 'Recency Days' }
  ], 'Customer profile', 24, 250, 1872, 190),
  chart(ids.customerDetail, 'orders-trend', 'lineChart', { entity: 'Date', property: 'Year Month' }, ['Sales / GMV'], 'Customer sales history', 24, 456, 910, 300, { tooltips: ['Orders', 'Average Order Value', 'Items per Order', 'Freight Ratio'] }),
  donut(ids.customerDetail, 'experience', { entity: 'Order', property: 'Delivery Performance' }, 'Orders', 'Delivery outcome mix', 950, 456, 946, 300),
  table(ids.customerDetail, 'orders', [
    { entity: 'Order', property: 'Order ID' },
    { entity: 'Order', property: 'Purchase Date Time' },
    { entity: 'Order', property: 'Status' },
    { entity: 'Order', property: 'Customer Order Type' },
    { entity: 'Order', property: 'Delivery Performance' },
    { entity: 'Order', property: 'Delivery Days' },
    { entity: 'Order', property: 'Delay Days' },
    { entity: 'Order', property: 'Gross Order Value' },
    { entity: 'Order', property: 'Freight Ratio' }
  ], 'Order-level detail', 24, 772, 1872, 284)
];

page(ids.executive, 'Executive Overview', executive);
page(ids.customer, 'Customer Deep Dive', customer);
page(ids.product, 'Product & Sales', product);
page(ids.delivery, 'Delivery & Experience', delivery);
page(ids.seller, 'Seller & Geography', seller);
page(ids.customerDetail, 'Customer Detail', customerDetail, {
  filterConfig: {
    filters: [{
      name: drillFilter,
      field: { Column: { Expression: { SourceRef: { Entity: 'Customer' } }, Property: 'Customer Display ID' } },
      type: 'Categorical',
      howCreated: 'Drillthrough'
    }]
  },
  pageBinding: {
    name: 'Pod',
    type: 'Drillthrough',
    parameters: [{
      name: `Param_${drillFilter}`,
      boundFilter: drillFilter,
      fieldExpr: { Column: { Expression: { SourceRef: { Entity: 'Customer' } }, Property: 'Customer Display ID' } }
    }]
  }
});

const pageOrder = Object.values(ids);
fs.writeFileSync(path.join(pagesRoot, 'pages.json'), JSON.stringify({
  $schema: 'https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.1.0/schema.json',
  pageOrder,
  activePageName: ids.executive
}, null, 2) + '\n');

const themeGuid = '63219f6b-8ed1-4dad-809f-6fbf529a3af4';
const themeFile = `OlistExecutive-${themeGuid}.json`;
const registeredDir = path.join(reportRoot, 'StaticResources', 'RegisteredResources');
fs.mkdirSync(registeredDir, { recursive: true });
const theme = {
  name: themeFile,
  dataColors: [colors.teal, colors.gold, colors.blue, colors.red, '#7A8B99', '#5C4D7D'],
  background: colors.canvas,
  foreground: colors.ink,
  tableAccent: colors.teal,
  textClasses: {
    title: { fontFace: 'Segoe UI Semibold', fontSize: 16, color: colors.ink },
    header: { fontFace: 'Segoe UI Semibold', fontSize: 11, color: '#34495E' },
    label: { fontFace: 'Segoe UI', fontSize: 10, color: colors.muted },
    callout: { fontFace: 'Segoe UI Semibold', fontSize: 26, color: colors.ink }
  },
  visualStyles: {
    '*': { '*': {
      background: [{ show: true, color: { solid: { color: colors.white } }, transparency: 0 }],
      border: [{ show: true, color: { solid: { color: colors.border } }, radius: 8, width: 1 }],
      visualHeader: [{ show: false }],
      title: [{ show: true, fontFamily: 'Segoe UI Semibold', fontSize: 11, fontColor: { solid: { color: colors.ink } } }]
    } },
    cardVisual: { '*': {
      title: [{ show: true }],
      background: [{ show: true, color: { solid: { color: colors.white } }, transparency: 0 }]
    } },
    tableEx: { '*': { values: [{ backColorSecondary: { solid: { color: '#F4F7F9' } } }] } },
    pivotTable: { '*': { values: [{ backColorSecondary: { solid: { color: '#E9F2F0' } } }] } }
  }
};
fs.writeFileSync(path.join(registeredDir, themeFile), JSON.stringify(theme, null, 2) + '\n');

const reportJsonPath = path.join(definitionRoot, 'report.json');
const report = JSON.parse(fs.readFileSync(reportJsonPath, 'utf8'));
report.themeCollection.customTheme = {
  name: themeFile,
  reportVersionAtImport: { visual: '2.12.0', report: '3.4.0', page: '2.3.1' },
  type: 'RegisteredResources'
};
report.resourcePackages = report.resourcePackages.filter((pkg) => pkg.name !== 'RegisteredResources');
report.resourcePackages.push({
  name: 'RegisteredResources',
  type: 'RegisteredResources',
  items: [{ name: themeFile, path: themeFile, type: 'CustomTheme' }]
});
fs.writeFileSync(reportJsonPath, JSON.stringify(report, null, 2) + '\n');

console.log(`Built ${pageOrder.length} pages with ${[executive, customer, product, delivery, seller, customerDetail].reduce((n, visuals) => n + visuals.length, 0)} visuals.`);
